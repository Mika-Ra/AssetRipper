using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.Import.Configuration;
using AssetRipper.Import.Logging;
using AssetRipper.SourceGenerated;

namespace AssetRipper.Export.UnityProjects
{
	public sealed partial class ProjectExporter
	{
		public event Action? EventExportPreparationStarted;
		public event Action? EventExportPreparationFinished;
		public event Action? EventExportStarted;
		public event Action<int, int>? EventExportProgressUpdated;
		public event Action? EventExportFinished;

		private readonly ObjectHandlerStack<IAssetExporter> assetExporterStack = new();

		//Exporters
		private DummyAssetExporter DummyExporter { get; } = new DummyAssetExporter();

		/// <summary>Adds an exporter to the stack of exporters for this asset type.</summary>
		/// <typeparam name="T">The c sharp type of this asset type. Any inherited types also get this exporter.</typeparam>
		/// <param name="exporter">The new exporter. If it doesn't work, the next one in the stack is used.</param>
		/// <param name="allowInheritance">Should types that inherit from this type also use the exporter?</param>
		public void OverrideExporter<T>(IAssetExporter exporter, bool allowInheritance = true)
		{
			assetExporterStack.OverrideHandler(typeof(T), exporter, allowInheritance);
		}

		/// <summary>Adds an exporter to the stack of exporters for this asset type.</summary>
		/// <param name="type">The c sharp type of this asset type. Any inherited types also get this exporter.</param>
		/// <param name="exporter">The new exporter. If it doesn't work, the next one in the stack is used.</param>
		/// <param name="allowInheritance">Should types that inherit from this type also use the exporter?</param>
		public void OverrideExporter(Type type, IAssetExporter exporter, bool allowInheritance)
		{
			assetExporterStack.OverrideHandler(type, exporter, allowInheritance);
		}

		/// <summary>
		/// Use the <see cref="DummyExporter"/> for the specified class type.
		/// </summary>
		/// <typeparam name="T">The base type for assets of that <paramref name="classType"/>.</typeparam>
		/// <param name="classType">The class id of assets we are using the <see cref="DummyExporter"/> for.</param>
		/// <param name="isEmptyCollection">
		/// True: an exception will be thrown if the asset is referenced by another asset.<br/>
		/// False: any references to this asset will be replaced with a missing reference.
		/// </param>
		/// <param name="isMetaType"><see cref="AssetType.Meta"/> or <see cref="AssetType.Serialized"/>?</param>
		private void OverrideDummyExporter<T>(ClassIDType classType, bool isEmptyCollection, bool isMetaType)
		{
			DummyExporter.SetUpClassType(classType, isEmptyCollection, isMetaType);
			OverrideExporter<T>(DummyExporter, true);
		}

		public AssetType ToExportType(Type type)
		{
			foreach (IAssetExporter exporter in assetExporterStack.GetHandlerStack(type))
			{
				if (exporter.ToUnknownExportType(type, out AssetType assetType))
				{
					return assetType;
				}
			}
			throw new NotSupportedException($"There is no exporter that know {nameof(AssetType)} for unknown asset '{type}'");
		}

		private IExportCollection CreateCollection(IUnityObjectBase asset)
		{
			foreach (IAssetExporter exporter in assetExporterStack.GetHandlerStack(asset.GetType()))
			{
				if (exporter.TryCreateCollection(asset, out IExportCollection? collection))
				{
					return collection;
				}
			}
			throw new Exception($"There is no exporter that can handle '{asset}'");
		}

		public void Export(GameBundle fileCollection, CoreConfiguration options, FileSystem fileSystem)
		{
			EventExportPreparationStarted?.Invoke();
			List<IExportCollection> collections = CreateCollections(fileCollection);
			
			// ========== 调试信息开始 ==========
			Logger.Info(LogCategory.Export, "=== DEBUG: Collection Analysis ===");
			Logger.Info(LogCategory.Export, $"Total collections: {collections.Count}");
			
			// 统计每种类型的集合
			var collectionStats = collections
				.GroupBy(c => c.GetType().Name)
				.Select(g => new { Type = g.Key, Count = g.Count() })
				.OrderByDescending(x => x.Count);
			
			Logger.Info(LogCategory.Export, "Collection types:");
			foreach (var stat in collectionStats)
			{
				Logger.Info(LogCategory.Export, $"  {stat.Type}: {stat.Count}");
			}
			
			// 详细分析前20个集合（或全部如果少于20个）
			Logger.Info(LogCategory.Export, "\n=== First 20 Collections Details ===");
			int debugLimit = Math.Min(20, collections.Count);
			for (int i = 0; i < debugLimit; i++)
			{
				var collection = collections[i];
				var collectionType = collection.GetType().Name;
				var collectionFullType = collection.GetType().FullName;
				
				Logger.Info(LogCategory.Export, $"\n[{i}] Collection: {collection.Name}");
				Logger.Info(LogCategory.Export, $"    Type: {collectionType}");
				Logger.Info(LogCategory.Export, $"    Full Type: {collectionFullType}");
				Logger.Info(LogCategory.Export, $"    Exportable: {collection.Exportable}");
				Logger.Info(LogCategory.Export, $"    Assets Count: {collection.Assets.Count()}");
				
				// 显示集合中的资源详情
				var assets = collection.Assets.Take(5).ToList(); // 只显示前5个资源
				if (assets.Any())
				{
					Logger.Info(LogCategory.Export, "    Assets:");
					foreach (var asset in assets)
					{
						Logger.Info(LogCategory.Export, $"      - ClassID: {asset.ClassID}, Type: {asset.GetType().Name}");
					}
					if (collection.Assets.Count() > 5)
					{
						Logger.Info(LogCategory.Export, $"      ... and {collection.Assets.Count() - 5} more assets");
					}
				}
			}
			
			// 特别标记 AnimationClip 相关的集合
			Logger.Info(LogCategory.Export, "\n=== AnimationClip Collections (ClassID 74) ===");
			var animClipCollections = collections
				.Where(c => c.Assets.Any(a => a.ClassID == 74))
				.ToList();
			
			if (animClipCollections.Any())
			{
				Logger.Info(LogCategory.Export, $"Found {animClipCollections.Count} collections containing AnimationClips:");
				foreach (var collection in animClipCollections.Take(10)) // 显示前10个
				{
					Logger.Info(LogCategory.Export, $"  - Name: {collection.Name}");
					Logger.Info(LogCategory.Export, $"    Type: {collection.GetType().Name}");
					Logger.Info(LogCategory.Export, $"    Full Type: {collection.GetType().FullName}");
					var animClips = collection.Assets.Where(a => a.ClassID == 74).ToList();
					Logger.Info(LogCategory.Export, $"    AnimationClips: {animClips.Count}");
				}
			}
			else
			{
				Logger.Warning(LogCategory.Export, "No AnimationClip collections found!");
			}
			
			Logger.Info(LogCategory.Export, "=== DEBUG END ===\n");
			// ========== 调试信息结束 ==========
			
			EventExportPreparationFinished?.Invoke();

			EventExportStarted?.Invoke();
			ProjectAssetContainer container = new ProjectAssetContainer(this, options, fileCollection.FetchAssets(), collections);
			int exportableCount = collections.Count(c => c.Exportable);
			int currentExportable = 0;

			for (int i = 0; i < collections.Count; i++)
			{
				IExportCollection collection = collections[i];
				container.CurrentCollection = collection;
				if (collection.Exportable)
				{
					currentExportable++;
					Logger.Info(LogCategory.ExportProgress, $"({currentExportable}/{exportableCount}) Exporting '{collection.Name}'");
					bool exportedSuccessfully = collection.Export(container, options.ProjectRootPath, fileSystem);
					if (!exportedSuccessfully)
					{
						Logger.Warning(LogCategory.ExportProgress, $"Failed to export '{collection.Name}' ({collection.GetType().Name})");
					}
				}
				EventExportProgressUpdated?.Invoke(i, collections.Count);
			}
			EventExportFinished?.Invoke();
		}

		private List<IExportCollection> CreateCollections(GameBundle fileCollection)
		{
			List<IExportCollection> collections = new();
			HashSet<IUnityObjectBase> queued = new();

			foreach (IUnityObjectBase asset in fileCollection.FetchAssets())
			{
				if (!queued.Contains(asset))
				{
					IExportCollection collection = CreateCollection(asset);
					foreach (IUnityObjectBase element in collection.Assets)
					{
						queued.Add(element);
					}
					collections.Add(collection);
				}
			}

			return collections;
		}
	}
}
