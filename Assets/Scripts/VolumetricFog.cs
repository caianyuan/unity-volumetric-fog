﻿using UnityEngine;
using UnityEngine.Rendering;

[ExecuteInEditMode]
    [RequireComponent (typeof(Camera))]
	class VolumetricFog : MonoBehaviour
	{
		

		[Header("Required assets")]
		
		[SerializeField] private Shader _CalculateFogShader;
		[SerializeField] private Shader _ApplyBlurShader;
		[SerializeField] private Shader _ApplyFogShader;
		[SerializeField] private Transform SunLight;
		[SerializeField] private Texture2D _FogTexture2D;

		[Header("Position and size(in m³)")] 
		
		[SerializeField] private bool _LimitFogInSize = true;
		[SerializeField] private Vector3 _FogWorldPosition;
		[SerializeField] private float _FogSize = 10.0f;

		[Header("Performance")]
		
		[SerializeField] [Range(1, 8)] private int _RenderTextureResDivision = 2;
		[SerializeField] [Range(16, 256)] private int _RayMarchSteps = 128;

		[SerializeField] private bool _UseInterleavedSampling = true;
		
		[Tooltip("Interleaved sampling square size")]
		[SerializeField] [Range(1, 16)] private int _SQRSize = 8;

		[Header("Physical coefficients")] 
		
		[SerializeField] private bool _UseRayleighScattering = true;
		[SerializeField] private float _RayleighScatteringCoef = 0.25f;

		[SerializeField] private float _MieScatteringCoef = 0.25f;

		[SerializeField]
		private MieScatteringApproximation _MieScatteringApproximation = MieScatteringApproximation.HenyeyGreenstein;
		
		[SerializeField] private float _FogDensityCoef = 0.3f;
		[SerializeField] private float _ExtinctionCoef = 0.01f;
		[SerializeField] [Range(-1,1)]  private float _Anisotropy = 0.5f;
		[SerializeField] private float _HeightDensityCoef = 0.5f;
		[SerializeField] private float _BaseHeightDensity = 0.5f;
		
		[Header("Blur")]
		
		[SerializeField] [Range(1, 8)] private int _BlurIterations = 4;
		[SerializeField] private float _BlurDepthFalloff = 0.5f;
		[SerializeField] private Vector3 _BlurOffsets = new Vector3(1, 2, 3);
		[SerializeField] private Vector3 _BlurWeights = new Vector3(0.213f, 0.17f, 0.036f);
		
		[Header("Color")]
		
		[SerializeField] private Color _ShadowColor = Color.black;
		[SerializeField] private Color _LightColor;
		[SerializeField] [Range(0,1)] private float _AmbientFog;
		
		[SerializeField] [Range(0,10)] private float _LightIntensity = 1;

		[Header("Debug")] 
		
		[SerializeField] private bool _AddSceneColor;
		[SerializeField] private bool _BlurEnabled;
		[SerializeField] private bool _ShadowsEnabled;
		[SerializeField] private bool _HeightFogEnabled;

		[SerializeField] private bool _Test;
		
		private Material _DownscaleDepthMaterial;//TODO
		private Material _ApplyBlurMaterial;
		private Material _CalculateFogMaterial;
		private Material _ApplyFogMaterial;

		public enum MieScatteringApproximation
		{
			HenyeyGreenstein,
			CornetteShanks,
			Off
		}


		public Material ApplyFogMaterial
		{
			get
			{
				if (!_ApplyFogMaterial && _ApplyFogShader)
				{
					_ApplyFogMaterial = new Material(_ApplyFogShader);
					_ApplyFogMaterial.hideFlags = HideFlags.HideAndDontSave;
				}

				return _ApplyFogMaterial;
			}
		}


		public Material ApplyBlurMaterial
		{
			get
			{
				if (!_ApplyBlurMaterial && _ApplyBlurShader)
				{
					_ApplyBlurMaterial = new Material(_ApplyBlurShader);
					_ApplyBlurMaterial.hideFlags = HideFlags.HideAndDontSave;
				}

				return _ApplyBlurMaterial;
			}
		}		
		
		public Material CalculateFogMaterial
		{
			get
			{
				if (!_CalculateFogMaterial && _CalculateFogShader)
				{
					_CalculateFogMaterial = new Material(_CalculateFogShader);
					_CalculateFogMaterial.hideFlags = HideFlags.HideAndDontSave;
				}

				return _CalculateFogMaterial;
			}
		}

		private Camera _CurrentCamera;
    
		public Camera CurrentCamera
		{
			get
			{
				if (!_CurrentCamera)
					_CurrentCamera = GetComponent<Camera>();
				return _CurrentCamera;
			}
		}

		private CommandBuffer _AfterShadowPass;
    
		void Start()
		{
			AddLightCommandBuffer();
		}
    
		void OnDestroy()
		{
			RemoveLightCommandBuffer();
		}

		
		// based on https://interplayoflight.wordpress.com/2015/07/03/adventures-in-postprocessing-with-unity/    
		private void RemoveLightCommandBuffer()
		{
			// TODO : SUPPORT MULTIPLE LIGHTS 

			Light light = null;
			if (SunLight != null)
			{
				light = SunLight.GetComponent<Light>();    
			}
        
			if (_AfterShadowPass != null && light)
			{
				light.RemoveCommandBuffer(LightEvent.AfterShadowMap, _AfterShadowPass);
			}
		}

		// based on https://interplayoflight.wordpress.com/2015/07/03/adventures-in-postprocessing-with-unity/
		void AddLightCommandBuffer()
		{
			_AfterShadowPass = new CommandBuffer();
			_AfterShadowPass.name = "Volumetric Fog ShadowMap";
			_AfterShadowPass.SetGlobalTexture("ShadowMap", new RenderTargetIdentifier(BuiltinRenderTextureType.CurrentActive));

			Light light = SunLight.GetComponent<Light>();
    
			if (light)
			{
				light.AddCommandBuffer(LightEvent.AfterShadowMap, _AfterShadowPass);
			}

		}

		private Texture3D _FogTexture3D;

		
		void OnRenderImage (RenderTexture source, RenderTexture destination)
		{
			if (!_FogTexture2D ||
			    !ApplyFogMaterial || !_ApplyFogShader ||
			    !CalculateFogMaterial || !_CalculateFogShader ||
			    !ApplyBlurMaterial || !_ApplyBlurShader)
			{
				Debug.Log("Not rendering image effect");
				Graphics.Blit(source, destination); // do nothing
				return;
			}

			if (!_FogTexture3D)
			{
			//	_FogTexture3D = TextureUtilites.CreateTexture3DFrom2DSlices(_FogTexture2D, 128);
			}

			
			
			if (_ShadowsEnabled)
			{
				Shader.EnableKeyword("SHADOWS_ON");
				Shader.DisableKeyword("SHADOWS_OFF");
			}
			else
			{
				Shader.DisableKeyword("SHADOWS_ON");
				Shader.EnableKeyword("SHADOWS_OFF");
			}
			
			
			SunLight.GetComponent<Light>().color = _LightColor;
			SunLight.GetComponent<Light>().intensity = _LightIntensity;
			
			
			RenderTextureFormat RTFogOutput = RenderTextureFormat.RFloat;
			int depthWidth = source.width;
			int depthHeight= source.height;

			RenderTexture depthRT = RenderTexture.GetTemporary (depthWidth, depthHeight, 0, RTFogOutput);
/*
			//downscale depth buffer to quarter resolution
			Graphics.Blit (source, lowresDepthRT, _DownscaleDepthMaterial);
			lowresDepthRT.filterMode = FilterMode.Point;
			*/
			RenderTextureFormat format = RenderTextureFormat.ARGBHalf;
			// float 4 : 1. A , 2: R, 3 : G, 4 : B
			
			int fogRTWidth= source.width / _RenderTextureResDivision;
			int fogRTHeight= source.height / _RenderTextureResDivision;


			RenderTexture fogRT1 = RenderTexture.GetTemporary (fogRTWidth, fogRTHeight, 0, format);
			RenderTexture fogRT2 = RenderTexture.GetTemporary (fogRTWidth, fogRTHeight, 0, format);

			fogRT1.filterMode = FilterMode.Bilinear;
			fogRT2.filterMode = FilterMode.Bilinear;

			Light light = SunLight.GetComponent<Light>();

			Shader.SetGlobalMatrix("InverseViewMatrix", CurrentCamera.cameraToWorldMatrix);
			Shader.SetGlobalMatrix("InverseProjectionMatrix", CurrentCamera.projectionMatrix.inverse);
			
			
			ToggleShaderKeyword(CalculateFogMaterial, "SHADOWS_ON", _ShadowsEnabled); // todo fix
			ToggleShaderKeyword(CalculateFogMaterial, "LIMITFOGSIZE", _LimitFogInSize);
			ToggleShaderKeyword(CalculateFogMaterial, "HEIGHTFOG", _HeightFogEnabled);
			ToggleShaderKeyword(CalculateFogMaterial, "INTERLEAVED_SAMPLING", _UseInterleavedSampling);
			
			
			if (_UseRayleighScattering)
			{
				CalculateFogMaterial.EnableKeyword("RAYLEIGH_SCATTERING");
				CalculateFogMaterial.SetFloat ("_RayleighScatteringCoef", _RayleighScatteringCoef);

			}
			else
			{
				CalculateFogMaterial.DisableKeyword("RAYLEIGH_SCATTERING");
			}


			switch (_MieScatteringApproximation)
			{
				case MieScatteringApproximation.HenyeyGreenstein:
					ToggleShaderKeyword(CalculateFogMaterial, "HG_SCATTERING", true);
					ToggleShaderKeyword(CalculateFogMaterial, "CS_SCATTERING", false);
					CalculateFogMaterial.SetFloat("_MieScatteringCoef", _MieScatteringCoef);
					break;

				case MieScatteringApproximation.CornetteShanks:

					ToggleShaderKeyword(CalculateFogMaterial, "CS_SCATTERING", true);
					ToggleShaderKeyword(CalculateFogMaterial, "HG_SCATTERING", false);

					CalculateFogMaterial.SetFloat("_MieScatteringCoef", _MieScatteringCoef);
					break;

				case MieScatteringApproximation.Off:
					ToggleShaderKeyword(CalculateFogMaterial, "HG_SCATTERING", false);
					ToggleShaderKeyword(CalculateFogMaterial, "CS_SCATTERING", false);
					break;
			}

			//		CalculateFogMaterial.SetVector("ExpRL", new Vector3((float)6.55e-6,(float)1.73e-5, (float)2.30e-5));
			
		//	CalculateFogMaterial.SetTexture ("LowResDepth", lowresDepthRT); TODO
			CalculateFogMaterial.SetTexture ("_NoiseTexture", _FogTexture2D);
			CalculateFogMaterial.SetTexture("_NoiseTex3D", _FogTexture3D);
			
			CalculateFogMaterial.SetFloat("_RaymarchSteps", _RayMarchSteps);
			CalculateFogMaterial.SetFloat("_InterleavedSamplingSQRSize", _SQRSize);


			CalculateFogMaterial.SetFloat ("_FogDensity", _FogDensityCoef);
			

			CalculateFogMaterial.SetFloat ("_ExtinctionCoef", _ExtinctionCoef);
			CalculateFogMaterial.SetFloat("_Anisotropy", _Anisotropy);
			CalculateFogMaterial.SetFloat("_BaseHeightDensity", _BaseHeightDensity);
			CalculateFogMaterial.SetFloat("_HeightDensityCoef", _HeightDensityCoef);
			
			CalculateFogMaterial.SetVector ("_FogWorldPosition", _FogWorldPosition);
			CalculateFogMaterial.SetFloat("_FogSize", _FogSize);
			CalculateFogMaterial.SetFloat ("_LightIntensity", _LightIntensity);
			CalculateFogMaterial.SetColor ("_ShadowColor", _ShadowColor);
			CalculateFogMaterial.SetVector ("_LightColor", _LightColor);
			CalculateFogMaterial.SetFloat("_AmbientFog", _AmbientFog);



			//render fog

			Graphics.Blit (source, fogRT1, CalculateFogMaterial);
			//Graphics.Blit (fogRT1, source, CalculateFogMaterial);
			Graphics.Blit(fogRT1, destination);

		
			if (_BlurEnabled)
			{
				
				ApplyBlurMaterial.SetFloat("_BlurDepthFalloff", _BlurDepthFalloff);
				
				Vector4 BlurOffsets =   new Vector4(0, // initial sample is always at the center 
													_BlurOffsets.x,
													_BlurOffsets.y,
													_BlurOffsets.z);
				
				ApplyBlurMaterial.SetVector("_BlurOffsets", BlurOffsets);

				// x is sum of all weights
				Vector4 BlurWeightsWithTotal =  new Vector4(_BlurWeights.x + _BlurWeights.y + _BlurWeights.z,
															_BlurWeights.x,
															_BlurWeights.y,
															_BlurWeights.z);
				
				ApplyBlurMaterial.SetVector("_BlurWeights", BlurWeightsWithTotal);
				//	ApplyBlurMaterial.SetTexture ("LowresDepthSampler", lowresDepthRT);


				for (int i = 0; i < _BlurIterations; i++)
				{
					
					// vertical blur 
					ApplyBlurMaterial.SetVector ("BlurDir", new Vector2(0,1));
					Graphics.Blit (fogRT1, fogRT2, ApplyBlurMaterial);

					// horizontal blur
					ApplyBlurMaterial.SetVector ("BlurDir", new Vector2(1,0));
					Graphics.Blit (fogRT2, fogRT1, ApplyBlurMaterial);

				}
	
			}



			if (_AddSceneColor)
			{
				//apply fog to main scene
			
				ApplyFogMaterial.SetTexture ("FogRendertargetPoint", fogRT1);
				ApplyFogMaterial.SetTexture ("FogRendertargetLinear", fogRT1);
			
				//	ApplyFogMaterial.SetTexture ("LowResDepthTexture", lowresDepthRT);
				//apply to main rendertarget
				Graphics.Blit (source, destination, ApplyFogMaterial);
			}

			
			RenderTexture.ReleaseTemporary(depthRT);
			RenderTexture.ReleaseTemporary(fogRT1);
			RenderTexture.ReleaseTemporary(fogRT2);

		}

		void ToggleShaderKeyword(Material shaderMat, string keyword, bool enabled)
		{
			if (enabled)
			{
				shaderMat.EnableKeyword(keyword);
			}
			else
			{
				shaderMat.DisableKeyword(keyword);
			}
		}

		
		
		
	}

