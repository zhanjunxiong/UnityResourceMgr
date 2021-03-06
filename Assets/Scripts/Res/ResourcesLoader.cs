/*----------------------------------------------------------------
// 模块名：Resources加载类封装
// 创建者：zengyi
// 修改者列表：
// 创建日期：2015年6月1日
// 模块描述：
//         1、用于测试环境资源加载
//		   2、已支持异步加载回调
//----------------------------------------------------------------*/

#define USE_HAS_EXT
//#define USE_UNLOADASSET

using System;
using System.Collections.Generic;
using UnityEngine;

public class ResourceAssetCache: AssetCache
{
#if UNITY_EDITOR
	// 用于在编辑器中标识用
	public string Tag
	{
		get
		{
			CheckTag();
			return mTag;
		}
	}
	
	void CheckTag()
	{
		if (mTarget == null)
			return;
		if (string.IsNullOrEmpty(mTag))
		{
			if (mTarget is GameObject)
				mTag = "obj";
			else
				if (mTarget is AudioClip)
					mTag = "audio";
			else
				if (mTarget is Texture)
					mTag = "tex";
			else
				if (mTarget is Shader)
					mTag = "shader";
			else
				if (mTarget is Material)
					mTag = "mat";
			else
				if (mTarget is RuntimeAnimatorController)
					mTag = "AniController";
			
			if (!string.IsNullOrEmpty(mTag))
			{
				string path = UnityEditor.AssetDatabase.GetAssetPath(mTarget);
				path = System.IO.Path.GetDirectoryName(path);
				mTag += "(" + path + ")";
			}
			
		}
	}
	
	private string mTag = string.Empty;
#endif
	public ResourceAssetCache(UnityEngine.Object target)
	{
		mTarget = target;
		mIsGameObject = (mTarget as GameObject) != null;
	}

	public UnityEngine.Object Target {
		get 
		{
			return mTarget;
		}
	}

	protected override void OnUnLoad()
	{
		if (mTarget != null) {
			if (mIsGameObject)
			{
				/*
				 * 使用GameObject.DestroyImmediate(mTarget, true) 会导致UnityEditor中 文件的操作无法使用,以及第二次使用Resources.Load失败
				 * GameObject.DestroyObject 和 GameObject.Destroy， 使用GameObject.DestroyImmediate(mTarget, false)会提示使用 DestroyImmediate(mTarget, true)
				 * 使用Resources.UnloadAsset 会提示错误：只能释放不可见的资源，不能是GameObject
				 */
	
				// GameObject.DestroyImmediate(mTarget, true);
				// GameObject.DestroyObject(mTarget);
			  	
				if (Application.isEditor)
					LogMgr.Instance.LogWarning("ResourceAssetCache OnUnLoad: GameObject is not UnLoad in EditorMode!");
			//	else
			//		Resources.UnloadAsset(mTarget);
					// GameObject.DestroyImmediate(mTarget, true);
			} else
			{
				// texture, material etc.
				if (mTarget != null)
				{
#if USE_UNLOADASSET
					Resources.UnloadAsset (mTarget);
#endif
				}
				//if (Application.isEditor)
				//	LogMgr.Instance.LogWarning("ResourceAssetCache OnUnLoad: GameObject is not UnLoad in EditorMode!");
				//GameObject.DestroyImmediate(mTarget, true);
			}
			mTarget = null;
		}
	}

	private UnityEngine.Object mTarget = null;
	private bool mIsGameObject = false;
}

public class ResourcesLoader: IResourceLoader
{
	private static readonly string cResourcesStartPath = "resources/";
	
	protected bool IsResLoaderFileName(ref string fileName)
	{
		if (string.IsNullOrEmpty (fileName))
			return false;
		if (fileName.StartsWith (cResourcesStartPath, StringComparison.CurrentCultureIgnoreCase)) {
			fileName = fileName.Remove (0, cResourcesStartPath.Length);
#if USE_HAS_EXT
			int idx = fileName.LastIndexOf('.');
			if (idx > 0)
			{
				fileName = fileName.Substring(0, idx);
			} else if (idx == 0)
				fileName = string.Empty;
#endif
			return true;
		}
		
		return false;
	}

	#region public function

	public T LoadObject<T>(string fileName, ResourceCacheType cacheType) where T: UnityEngine.Object
	{
		if (string.IsNullOrEmpty (fileName))
			return null;

		T ret = null;
		if (IsResLoaderFileName (ref fileName))
			ret = Resources.Load<T>(fileName);
		else {
			return null;
		}
		if (ret && (cacheType != ResourceCacheType.rctNone)) {
			AssetCache cache = AssetCacheManager.Instance.FindOrgObjCache (ret);
			if (cache == null)
				cache = ResourceMgr.Instance.ResLoader.CreateCache (ret, fileName);
			if (cache != null) {
				if (cacheType == ResourceCacheType.rctRefAdd)
					AssetCacheManager.Instance._AddOrUpdateUsedList (cache);
				AssetCacheManager.Instance._OnLoadObject (ret, cache);
			}
		} else if (ret && (cacheType != ResourceCacheType.rctNone)) {
			AssetCache cache = AssetCacheManager.Instance.FindOrgObjCache (ret);
			if (cache == null)
			{
				// 第一次加载
				cache = ResourceMgr.Instance.ResLoader.CreateCache (ret, fileName);
				if (cache != null)
				{
					AssetCacheManager.Instance._AddTempAsset(cache);
				}
			}
		}
		return ret;
	}

	public bool LoadObjectAsync<T>(string fileName, ResourceCacheType cacheType, Action<float, bool, T> onProcess) where T: UnityEngine.Object
	{
		if (string.IsNullOrEmpty (fileName))
			return false;

		if (!IsResLoaderFileName (ref fileName)) {
			if (!Application.isEditor)
				return false;

			/*
			T obj = LoadObject<T>(fileName, cacheType);
			if (obj == null)
			{
				if (onProcess != null)
					onProcess(0, true, obj);
				return false;
			}

			if (onProcess != null)
				onProcess(1.0f, true, obj);

			return true;*/
			return false;
		}

		// 同时反复调用可能产生多个Timer
		ResourceRequest request = Resources.LoadAsync (fileName, typeof(T));
		if (request == null)
			return false;
		if (request.isDone) {
			T orgObj = request.asset as T;
			if (orgObj == null)
			{
				string err = string.Format("LoadObjectAsync: ({0}) error!", fileName);
				LogMgr.Instance.LogError(err);
				return false;
			}
			if (onProcess != null)
				onProcess(request.progress, request.isDone, orgObj);
			return true;
		}

		var ret = AsyncOperationMgr.Instance.AddAsyncOperation<ResourceRequest> (request,
		                                              delegate (ResourceRequest req) {
			if (req.isDone)
			{
				T orgObj = req.asset as T;
				if (orgObj == null)
				{
					string err = string.Format("LoadObjectAsync: ({0}) error!", fileName);
					LogMgr.Instance.LogError(err);
					return;
				}

				if (orgObj != null && (cacheType != ResourceCacheType.rctNone)) {
					AssetCache cache = AssetCacheManager.Instance.FindOrgObjCache(orgObj);
					if (cache == null)
						cache = ResourceMgr.Instance.ResLoader.CreateCache(orgObj, fileName);
					if (cache != null)
					{
						if (cacheType == ResourceCacheType.rctRefAdd)
							AssetCacheManager.Instance._AddOrUpdateUsedList(cache);
						AssetCacheManager.Instance._OnLoadObject(orgObj, cache);
					}
				} else if ((orgObj != null) && (cacheType == ResourceCacheType.rctNone))
				{
					AssetCache cache = AssetCacheManager.Instance.FindOrgObjCache(orgObj);
					if (cache == null)
					{
						// 第一次加载
						cache = ResourceMgr.Instance.ResLoader.CreateCache(orgObj, fileName);
						if (cache != null)
							AssetCacheManager.Instance._AddTempAsset(cache);
					}
				}

				if (onProcess != null)
					onProcess(req.progress, req.isDone, orgObj);
			} else
			{
				if (onProcess != null)
					onProcess(req.progress, req.isDone, null);
			}
			
		}
		);
		
		return ret != null;
	}

	public override Shader LoadShader(string fileName, ResourceCacheType cacheType)
	{
		return LoadObject<Shader>(fileName, cacheType);
	}

	public override bool LoadShaderAsync(string fileName, ResourceCacheType cacheType, Action<float, bool, Shader> onProcess)
	{
		return LoadObjectAsync<Shader> (fileName, cacheType, onProcess);
	}

	public override GameObject LoadPrefab(string fileName, ResourceCacheType cacheType)
	{
		return LoadObject<GameObject> (fileName, cacheType);
	}

	public override bool LoadPrefabAsync(string fileName, ResourceCacheType cacheType, Action<float, bool, GameObject> onProcess)
	{
		return LoadObjectAsync<GameObject> (fileName, cacheType, onProcess);
	}

	public override AudioClip LoadAudioClip(string fileName, ResourceCacheType cache)
	{
		return LoadObject<AudioClip> (fileName, cache);
	}

	public override bool LoadAudioClipAsync(string fileName, ResourceCacheType cache, Action<float, bool, AudioClip> onProcess)
	{
		return LoadObjectAsync<AudioClip> (fileName, cache, onProcess);
	}

	public override string LoadText(string fileName, ResourceCacheType cache)
	{
		TextAsset text = LoadObject<TextAsset>(fileName, cache);
		if (text == null)
			return null;
		return System.Text.Encoding.UTF8.GetString (text.bytes);
	}

	public override byte[] LoadBytes(string fileName, ResourceCacheType cache)
	{
		TextAsset text = LoadObject<TextAsset>(fileName, cache);
		if (text == null)
			return null;
		return text.bytes;
	}

	public override bool LoadTextAsync (string fileName, ResourceCacheType cache, Action<float, bool, TextAsset> onProcess)
	{
		return LoadObjectAsync<TextAsset> (fileName, cache, onProcess);
	}

	// not used addToCache
	public override Material LoadMaterial(string fileName, ResourceCacheType cacheType)
	{
		return LoadObject<Material> (fileName, cacheType);
	}

	// not used addToCache
	public override bool LoadMaterialAsync(string fileName, ResourceCacheType cacheType, Action<float, bool, Material> onProcess)
	{
		return LoadObjectAsync<Material> (fileName, cacheType, onProcess);
	}

	// not used addToCache
	public override Texture LoadTexture(string fileName, ResourceCacheType cacheType)
	{
		return LoadObject<Texture> (fileName, cacheType);
	}

	// not used addToCache
	public override bool LoadTextureAsync(string fileName, ResourceCacheType cacheType, Action<float, bool, Texture> onProcess)
	{
		return LoadObjectAsync<Texture> (fileName, cacheType, onProcess);
	}

	public override RuntimeAnimatorController LoadAniController(string fileName, ResourceCacheType cacheType)
	{
		return LoadObject<RuntimeAnimatorController> (fileName, cacheType);
	}

	public override bool LoadAniControllerAsync(string fileName, ResourceCacheType cacheType, Action<float, bool, RuntimeAnimatorController> onProcess)
	{
		return LoadObjectAsync<RuntimeAnimatorController> (fileName, cacheType, onProcess);
	}

	public override AnimationClip LoadAnimationClip(string fileName, ResourceCacheType cacheType)
	{
		return LoadObject<AnimationClip> (fileName, cacheType);
	}

	public override bool LoadAnimationClipAsync(string fileName, ResourceCacheType cacheType, Action<float, bool, AnimationClip> onProcess)
	{
		return LoadObjectAsync<AnimationClip> (fileName, cacheType, onProcess);
	}

#if UNITY_5
	public override ShaderVariantCollection LoadShaderVarCollection(string fileName, 
	                                                                ResourceCacheType cacheType)
	{
		return LoadObject<ShaderVariantCollection> (fileName, cacheType);
	}
	
	public override bool LoadShaderVarCollectionAsync(string fileName, ResourceCacheType cacheType, 
	                                                  Action<float, bool, ShaderVariantCollection> onProcess)
	{
		return LoadObjectAsync<ShaderVariantCollection> (fileName, cacheType, onProcess);
	}
#endif	

	public override bool OnSceneLoad(string sceneName)
	{
		// 不建议将Scene放到Resources目录下，不好做对资源引用计数管理
		// LogMgr.Instance.LogWarning ("Don't load scene From Resources: because AssetCache maybe error!");
		return true;
	}

	public override bool OnSceneLoadAsync(string sceneName, Action onEnd)
	{
		if (onEnd != null)
			onEnd();
		return true;
	}

	public override bool OnSceneClose(string sceneName)
	{
		if (string.IsNullOrEmpty (sceneName))
			return false;
		return true;
	}

	public override AssetCache CreateCache(UnityEngine.Object orgObj, string fileName)
	{
		if (orgObj == null)
			return null;

		ResourceAssetCache cache = new ResourceAssetCache(orgObj);
		return cache;
	}

	#endregion public function
}