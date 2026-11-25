using System.Buffers.Binary;
using OpenElexSharp.Graphics;

namespace OpenElexSharp.Assets;

/// <summary>Headless output for the <c>assets</c> inspector commands.</summary>
public static class AssetCatalogConsole
{
	/// <summary>Runs a parsed asset command and returns a process exit code.</summary>
	public static int Run(
		AssetCommandLine command,
		AssetCatalog catalog,
		AssetFileSystem assets,
		TextWriter output,
		TextWriter error)
	{
		ArgumentNullException.ThrowIfNull(command);
		ArgumentNullException.ThrowIfNull(catalog);
		ArgumentNullException.ThrowIfNull(assets);
		ArgumentNullException.ThrowIfNull(output);
		ArgumentNullException.ThrowIfNull(error);

		return command.Kind switch
		{
			AssetCommandKind.Help => WriteUsage(output),
			AssetCommandKind.List => List(command, catalog, output, error),
			AssetCommandKind.Stats => Stats(catalog, output),
			AssetCommandKind.Export => Export(command, catalog, assets, output, error),
			AssetCommandKind.Resources => Resources(command, catalog, assets, output, error),
			AssetCommandKind.Objects => Objects(command, catalog, assets, output, error),
			AssetCommandKind.Templates => Templates(command, catalog, assets, output, error),
			AssetCommandKind.World => Worlds(command, catalog, assets, output, error),
			AssetCommandKind.Scene => Scenes(command, catalog, assets, output, error),
			AssetCommandKind.Collision => Collisions(command, catalog, assets, output, error),
			AssetCommandKind.Occlusion => Occlusions(command, catalog, assets, output, error),
			AssetCommandKind.Navigation => Navigations(command, catalog, assets, output, error),
			AssetCommandKind.Sounds => Sounds(command, catalog, assets, output, error),
			AssetCommandKind.Strings => Strings(command, catalog, assets, output, error),
			AssetCommandKind.Textures => Textures(command, catalog, assets, output, error),
			AssetCommandKind.Meshes => Meshes(command, catalog, assets, output, error),
			AssetCommandKind.Materials => Materials(command, catalog, assets, output, error),
			AssetCommandKind.Skeletons => Skeletons(command, catalog, assets, output, error),
			AssetCommandKind.Animations => Animations(command, catalog, assets, output, error),
			AssetCommandKind.BlendShapes => BlendShapes(command, catalog, assets, output, error),
			_ => throw new ArgumentOutOfRangeException(nameof(command)),
		};
	}

	private static int WriteUsage(TextWriter output)
	{
		output.WriteLine(AssetCommandLine.Usage);
		return 0;
	}

	private static int List(AssetCommandLine command, AssetCatalog catalog, TextWriter output, TextWriter error)
	{
		IEnumerable<AssetCatalogEntry> query = catalog.Entries;
		if (!command.IncludeBinaries)
		{
			query = query.Where(entry => !entry.IsExecutable);
		}

		if (!string.IsNullOrEmpty(command.Filter))
		{
			query = query.Where(entry =>
				entry.VirtualPath.Contains(command.Filter, StringComparison.OrdinalIgnoreCase));
		}

		if (!string.IsNullOrEmpty(command.Extension))
		{
			query = query.Where(entry =>
				string.Equals(entry.Extension, command.Extension, StringComparison.OrdinalIgnoreCase));
		}

		if (!string.IsNullOrEmpty(command.Source))
		{
			query = query.Where(entry =>
				entry.SourceName.Contains(command.Source, StringComparison.OrdinalIgnoreCase));
		}

		if (!string.IsNullOrEmpty(command.Language))
		{
			query = query.Where(entry =>
				string.Equals(entry.Language, command.Language, StringComparison.OrdinalIgnoreCase));
		}

		var matches = query.ToList();
		var shown = command.Limit == 0 ? matches.Count : Math.Min(command.Limit, matches.Count);
		for (var index = 0; index < shown; index++)
		{
			var entry = matches[index];
			output.WriteLine($"{entry.VirtualPath}\t{entry.Length}\t{entry.SourceName}");
		}

		error.WriteLine(
			$"Showing {shown} of {matches.Count} matching asset(s); catalog has {catalog.Entries.Count:N0} entries.");
		return 0;
	}

	private static int Stats(AssetCatalog catalog, TextWriter output)
	{
		var stats = catalog.GetStatistics();
		output.WriteLine($"Assets: {stats.TotalCount:N0} (packed {stats.PackedCount:N0}, loose {stats.LooseCount:N0})");
		output.WriteLine($"Executables: {stats.ExecutableCount:N0}");
		output.WriteLine($"Total size: {FormatBytes(stats.TotalBytes)}");
		output.WriteLine();
		WriteGroups(output, "By extension:", stats.Extensions);
		output.WriteLine();
		WriteGroups(output, "By source:", stats.Sources);
		return 0;
	}

	private static int Resources(
		AssetCommandLine command,
		AssetCatalog catalog,
		AssetFileSystem assets,
		TextWriter output,
		TextWriter error)
	{
		var index = ElexResourceIndex.Build(catalog, assets);
		foreach (var diagnostic in index.Diagnostics.Take(5))
		{
			error.WriteLine($"Resource warning: {diagnostic}");
		}

		IEnumerable<ElexResource> query = index.Resources;
		if (!string.IsNullOrEmpty(command.Filter))
		{
			query = query.Where(resource =>
				resource.Name.Contains(command.Filter, StringComparison.OrdinalIgnoreCase) ||
				resource.LogicalPath.Contains(command.Filter, StringComparison.OrdinalIgnoreCase));
		}

		if (!string.IsNullOrEmpty(command.TypeToken))
		{
			query = query.Where(resource =>
				string.Equals(resource.TypeToken, command.TypeToken, StringComparison.OrdinalIgnoreCase));
		}

		if (!string.IsNullOrEmpty(command.Language))
		{
			query = query.Where(resource =>
				string.Equals(resource.Language, command.Language, StringComparison.OrdinalIgnoreCase));
		}

		var matches = query.ToList();
		var shown = command.Limit == 0 ? matches.Count : Math.Min(command.Limit, matches.Count);
		for (var position = 0; position < shown; position++)
		{
			var resource = matches[position];
			output.WriteLine(
				$"{resource.Name}\t{resource.Size}\t{resource.TypeToken}\t{resource.Language}\t" +
				$"{resource.LogicalPath}\t{resource.VirtualPath}");
		}

		error.WriteLine(
			$"Showing {shown} of {matches.Count} resource(s); index has {index.Count:N0} resolved resources.");
		return 0;
	}

	private static int Objects(
		AssetCommandLine command,
		AssetCatalog catalog,
		AssetFileSystem assets,
		TextWriter output,
		TextWriter error)
	{
		const long MaxObjectFileSize = 128L * 1024 * 1024;

		if (command.AssetPath is null)
		{
			error.WriteLine("objects requires an asset path.");
			return 3;
		}

		if (!catalog.TryGetEntry(command.AssetPath, out var entry))
		{
			error.WriteLine($"Asset '{command.AssetPath}' was not found.");
			return 3;
		}

		if (entry.IsExecutable)
		{
			error.WriteLine($"Refusing to read executable file '{entry.VirtualPath}'.");
			return 3;
		}

		if (entry.Length > MaxObjectFileSize)
		{
			error.WriteLine($"Asset '{entry.VirtualPath}' is too large to inspect ({FormatBytes(entry.Length)}).");
			return 3;
		}

		try
		{
			using var stream = assets.OpenRead(entry.VirtualPath);
			using var buffer = new MemoryStream();
			stream.CopyTo(buffer);
			var data = buffer.ToArray();

			if (GenomeStringTable.IsGenomeFile(data))
			{
				var table = GenomeStringTable.Parse(data);
				output.WriteLine(
					$"GENOMFLE\t{table.Strings.Count}\t{table.BodySize}\t{table.Version}");
				error.WriteLine($"String table with {table.Strings.Count:N0} strings; no top-level objects.");
				return 0;
			}

			if (data.Length >= GenomeObject.HeaderSize &&
				BinaryPrimitives.ReadUInt32LittleEndian(data) == GenomeObject.Magic)
			{
				var genomeObject = GenomeObject.Parse(data, 0);
				WriteObject(output, "<object>", genomeObject);
				error.WriteLine("One top-level object.");
				return 0;
			}

			if (data.Length >= 12 &&
				BinaryPrimitives.ReadUInt32LittleEndian(data) == Gar5Header.Magic &&
				string.Equals(System.Text.Encoding.Latin1.GetString(data, 8, 4), GenomeHeaderDocument.Tag,
					StringComparison.Ordinal))
			{
				var document = GenomeHeaderDocument.Parse(data);
				var objects = document.Sections.SelectMany(section => section.Entries).ToList();
				var shown = command.Limit == 0 ? objects.Count : Math.Min(command.Limit, objects.Count);
				for (var index = 0; index < shown; index++)
				{
					var item = objects[index];
					WriteObject(output, item.Name, item.Object);
					if (command.ShowProperties)
					{
						foreach (var property in item.Object.Properties)
						{
							var propertyName = GenomeNameTable.GetNameOrHash(property.NameHash);
							var decoded = GenomePropertyDecoder.Decode(property);
							output.WriteLine(
								$"\t{propertyName}\t{decoded.TypeName}\t{GenomePropertyDecoder.Format(decoded)}");
						}
					}
				}

				var nestedCount = objects.Sum(item => item.Object.NestedObjects.Count);
				error.WriteLine(
					$"Showing {shown} of {objects.Count:N0} object(s) in {document.Sections.Count:N0} section(s); " +
					$"{nestedCount:N0} nested object(s).");
				return 0;
			}

			error.WriteLine($"Asset '{entry.VirtualPath}' is not a DT01 document, GEC2 object or GENOMFLE archive.");
			return 3;
		}
		catch (Exception exception) when (
			exception is GenomeFormatException or IOException or UnauthorizedAccessException or
				AssetNotFoundException)
		{
			error.WriteLine(exception.Message);
			return 3;
		}
	}

	private static void WriteObject(TextWriter output, string name, GenomeObject genomeObject)
	{
		output.WriteLine(
			$"{name}\t0x{genomeObject.ClassHash:x8}\t{genomeObject.Version}\t{genomeObject.Size}\t" +
			$"{genomeObject.Properties.Count}\t{genomeObject.ClassData.Count}\t{genomeObject.NestedObjects.Count}");
	}

	private static int Templates(
		AssetCommandLine command,
		AssetCatalog catalog,
		AssetFileSystem assets,
		TextWriter output,
		TextWriter error)
	{
		var database = TemplateDatabase.Build(catalog, assets);
		foreach (var diagnostic in database.Diagnostics.Take(5))
		{
			error.WriteLine($"Template warning: {diagnostic}");
		}

		IEnumerable<GenomeTemplate> query = database.Templates;
		if (!string.IsNullOrEmpty(command.Filter))
		{
			query = query.Where(template =>
				template.Name.Contains(command.Filter, StringComparison.OrdinalIgnoreCase) ||
				template.EntityName.Contains(command.Filter, StringComparison.OrdinalIgnoreCase) ||
				template.Guid.ToString("D").Contains(command.Filter, StringComparison.OrdinalIgnoreCase));
		}

		var matches = query.ToList();
		var shown = command.Limit == 0 ? matches.Count : Math.Min(command.Limit, matches.Count);
		for (var index = 0; index < shown; index++)
		{
			var template = matches[index];
			var parent = template.Parent?.Name ??
						 (template.ParentGuid == Guid.Empty ? "" : template.ParentGuid.ToString("D"));
			output.WriteLine(
				$"{template.Name}\t{template.Guid:D}\t{parent}\t{template.Depth}\t" +
				$"{template.PropertySets.Count}\t{template.GetEffectivePropertySets().Count}\t" +
				$"{template.GetAllPropertySets().Count}\t{template.Children.Count}");

			if (command.ShowProperties)
			{
				foreach (var propertySet in template.GetAllPropertySets())
				{
					var className = GenomeNameTable.GetNameOrHash(propertySet.ClassHash);
					output.WriteLine($"\t{className}\t{propertySet.Version}\t{propertySet.Properties.Count}");
					foreach (var property in propertySet.Properties)
					{
						var propertyName = GenomeNameTable.GetNameOrHash(property.NameHash);
						var decoded = GenomePropertyDecoder.Decode(property);
						output.WriteLine(
							$"\t\t{propertyName}\t{decoded.TypeName}\t{GenomePropertyDecoder.Format(decoded)}");
					}
				}
			}
		}

		error.WriteLine(
			$"Showing {shown} of {matches.Count:N0} template(s); database has " +
			$"{database.Templates.Count:N0} top-level and {database.AllTemplates.Count:N0} total templates.");
		return 0;
	}

	private static int Worlds(
		AssetCommandLine command,
		AssetCatalog catalog,
		AssetFileSystem assets,
		TextWriter output,
		TextWriter error)
	{
		var resources = ElexResourceIndex.Build(catalog, assets);
		foreach (var diagnostic in resources.Diagnostics.Take(5))
		{
			error.WriteLine($"World warning: {diagnostic}");
		}

		IEnumerable<ElexResource> query = resources.Resources.Where(resource =>
			string.Equals(resource.TypeToken, "wrl", StringComparison.OrdinalIgnoreCase));
		if (!string.IsNullOrEmpty(command.Filter))
		{
			query = query.Where(resource =>
				resource.Name.Contains(command.Filter, StringComparison.OrdinalIgnoreCase));
		}

		var matches = query
			.OrderBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();
		var shown = command.Limit == 0 ? matches.Count : Math.Min(command.Limit, matches.Count);
		var parsed = 0;
		for (var index = 0; index < shown; index++)
		{
			var resource = matches[index];
			ElexWorld world;
			try
			{
				world = ElexWorld.Parse(resource.Name, resource.VirtualPath, ReadAsset(assets, resource));
				parsed++;
			}
			catch (Exception exception) when (
				exception is GenomeFormatException or IOException or UnauthorizedAccessException or
					AssetNotFoundException)
			{
				error.WriteLine($"Skipping world '{resource.Name}': {exception.Message}");
				continue;
			}

			output.WriteLine(
				$"{world.Name}\t{world.SectorNames.Count}\t{world.TerrainFile}\t{world.SourcePath}");
			if (command.ShowProperties)
			{
				foreach (var sector in world.SectorNames)
				{
					output.WriteLine($"\tsector\t{sector}");
				}
			}
		}

		error.WriteLine($"Showing {shown} of {matches.Count:N0} world(s); parsed {parsed:N0}.");
		return 0;
	}

	private static int Scenes(
		AssetCommandLine command,
		AssetCatalog catalog,
		AssetFileSystem assets,
		TextWriter output,
		TextWriter error)
	{
		var resources = ElexResourceIndex.Build(catalog, assets);
		foreach (var diagnostic in resources.Diagnostics.Take(5))
		{
			error.WriteLine($"Scene warning: {diagnostic}");
		}

		IEnumerable<ElexResource> query = resources.Resources.Where(resource =>
			string.Equals(resource.TypeToken, "sec", StringComparison.OrdinalIgnoreCase));
		if (!string.IsNullOrEmpty(command.Filter))
		{
			query = query.Where(resource =>
				resource.Name.Contains(command.Filter, StringComparison.OrdinalIgnoreCase));
		}

		var matches = query
			.OrderBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();
		var shown = command.Limit == 0 ? matches.Count : Math.Min(command.Limit, matches.Count);
		var parsed = 0;
		for (var index = 0; index < shown; index++)
		{
			var resource = matches[index];
			ElexScene scene;
			try
			{
				scene = ElexScene.Parse(resource.Name, resource.VirtualPath, ReadAsset(assets, resource));
				parsed++;
			}
			catch (Exception exception) when (
				exception is GenomeFormatException or IOException or UnauthorizedAccessException or
					AssetNotFoundException)
			{
				error.WriteLine($"Skipping scene '{resource.Name}': {exception.Message}");
				continue;
			}

			output.WriteLine(
				$"{scene.Name}\t{scene.Entities.Count}\t{scene.MeshEntityCount}\t{scene.SourcePath}");
			if (!command.ShowProperties)
			{
				continue;
			}

			foreach (var entity in scene.Entities)
			{
				var position = entity.WorldPosition;
				var rotation = entity.WorldRotation;
				output.WriteLine(
					$"\tentity\t{entity.Depth}\t{entity.Name}\t" +
					$"{position.X:0.###},{position.Y:0.###},{position.Z:0.###}\t" +
					$"{rotation.X:0.#####},{rotation.Y:0.#####},{rotation.Z:0.#####},{rotation.W:0.#####}\t" +
					$"{entity.MeshFileName ?? "-"}\t" +
					$"{entity.TemplateGuid?.ToString("D") ?? "-"}\t" +
					$"{entity.NetworkResource ?? "-"}\t" +
					$"{(entity.CollisionShapes.Count == 0 ? "-" : string.Join(';', entity.CollisionShapes.Select(shape => shape.ResourceName ?? "inline")))}\t" +
					$"{(entity.IsTransformFinite ? "ok" : "non-finite")}");
				foreach (var propertySet in entity.PropertySets)
				{
					output.WriteLine(
						$"\t\tset\t{GenomeNameTable.GetNameOrHash(propertySet.ClassHash)}\t" +
						$"{propertySet.Version}\t{propertySet.Properties.Count}");
					foreach (var property in propertySet.Properties)
					{
						var decoded = GenomePropertyDecoder.Decode(property);
						output.WriteLine(
							$"\t\t\tprop\t{GenomeNameTable.GetNameOrHash(property.NameHash)}\t" +
							$"{decoded.TypeName}\t{GenomePropertyDecoder.Format(decoded)}");
					}
				}

				foreach (var reference in entity.References)
				{
					var value = reference.ReferenceGuid?.ToString("D") ?? reference.Resource ?? "-";
					output.WriteLine(
						$"\t\tref\t{reference.PropertySet}\t{reference.Property}\t" +
						$"{reference.TypeName}\t{value}");
				}

				foreach (var shape in entity.CollisionShapes)
				{
					var translation = shape.LocalPose.Translation;
					output.WriteLine(
						$"\t\tcollision\t{shape.ResourceName ?? "-"}\t" +
						$"{translation.X:0.###},{translation.Y:0.###},{translation.Z:0.###}");
				}
			}
		}

		error.WriteLine($"Showing {shown} of {matches.Count:N0} scene(s); parsed {parsed:N0}.");
		return 0;
	}

	private static int Collisions(
		AssetCommandLine command,
		AssetCatalog catalog,
		AssetFileSystem assets,
		TextWriter output,
		TextWriter error)
	{
		var resources = ElexResourceIndex.Build(catalog, assets);
		foreach (var diagnostic in resources.Diagnostics.Take(5))
		{
			error.WriteLine($"Collision warning: {diagnostic}");
		}

		IEnumerable<ElexResource> query = resources.Resources.Where(resource =>
			string.Equals(resource.TypeToken, "col", StringComparison.OrdinalIgnoreCase));
		if (!string.IsNullOrEmpty(command.Filter))
		{
			query = query.Where(resource =>
				resource.Name.Contains(command.Filter, StringComparison.OrdinalIgnoreCase));
		}

		var matches = query
			.OrderBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();
		var shown = command.Limit == 0 ? matches.Count : Math.Min(command.Limit, matches.Count);
		var parsed = 0;
		for (var index = 0; index < shown; index++)
		{
			var resource = matches[index];
			ElexCollisionMesh mesh;
			try
			{
				mesh = ElexCollisionMesh.Parse(resource.Name, resource.VirtualPath, ReadAsset(assets, resource));
				parsed++;
			}
			catch (Exception exception) when (
				exception is GenomeFormatException or IOException or UnauthorizedAccessException or
					AssetNotFoundException)
			{
				error.WriteLine($"Skipping collision mesh '{resource.Name}': {exception.Message}");
				continue;
			}

			var min = mesh.BoundsMin;
			var max = mesh.BoundsMax;
			output.WriteLine(
				$"{mesh.Name}\t{mesh.Vertices.Count}\t{mesh.TriangleCount}\t" +
				$"{min.X:0.###},{min.Y:0.###},{min.Z:0.###}\t" +
				$"{max.X:0.###},{max.Y:0.###},{max.Z:0.###}\t{mesh.SourcePath}");
		}

		error.WriteLine($"Showing {shown} of {matches.Count:N0} collision mesh(es); parsed {parsed:N0}.");
		return 0;
	}

	private static int Occlusions(
		AssetCommandLine command,
		AssetCatalog catalog,
		AssetFileSystem assets,
		TextWriter output,
		TextWriter error)
	{
		var resources = ElexResourceIndex.Build(catalog, assets);
		foreach (var diagnostic in resources.Diagnostics.Take(5))
		{
			error.WriteLine($"Occlusion warning: {diagnostic}");
		}

		IEnumerable<ElexResource> query = resources.Resources.Where(resource =>
			string.Equals(resource.TypeToken, "ocm", StringComparison.OrdinalIgnoreCase));
		if (!string.IsNullOrEmpty(command.Filter))
		{
			query = query.Where(resource =>
				resource.Name.Contains(command.Filter, StringComparison.OrdinalIgnoreCase));
		}

		var matches = query
			.OrderBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();
		var shown = command.Limit == 0 ? matches.Count : Math.Min(command.Limit, matches.Count);
		var parsed = 0;
		for (var index = 0; index < shown; index++)
		{
			var resource = matches[index];
			ElexOcclusionMesh mesh;
			try
			{
				mesh = ElexOcclusionMesh.Parse(resource.Name, resource.VirtualPath, ReadAsset(assets, resource));
				parsed++;
			}
			catch (Exception exception) when (
				exception is GenomeFormatException or IOException or UnauthorizedAccessException or
					AssetNotFoundException)
			{
				error.WriteLine($"Skipping occlusion mesh '{resource.Name}': {exception.Message}");
				continue;
			}

			var min = mesh.BoundsMin;
			var max = mesh.BoundsMax;
			output.WriteLine(
				$"{mesh.Name}\t{mesh.Vertices.Count}\t{mesh.TriangleCount}\t" +
				$"{min.X:0.###},{min.Y:0.###},{min.Z:0.###}\t" +
				$"{max.X:0.###},{max.Y:0.###},{max.Z:0.###}\t{mesh.SourcePath}");
		}

		error.WriteLine($"Showing {shown} of {matches.Count:N0} occlusion mesh(es); parsed {parsed:N0}.");
		return 0;
	}

	private static int Navigations(
		AssetCommandLine command,
		AssetCatalog catalog,
		AssetFileSystem assets,
		TextWriter output,
		TextWriter error)
	{
		var resources = ElexResourceIndex.Build(catalog, assets);
		foreach (var diagnostic in resources.Diagnostics.Take(5))
		{
			error.WriteLine($"Navigation warning: {diagnostic}");
		}

		IEnumerable<ElexResource> query = resources.Resources.Where(resource =>
			string.Equals(resource.TypeToken, "sec", StringComparison.OrdinalIgnoreCase) &&
			resource.Name.Contains("Nav", StringComparison.OrdinalIgnoreCase));
		if (!string.IsNullOrEmpty(command.Filter))
		{
			query = query.Where(resource =>
				resource.Name.Contains(command.Filter, StringComparison.OrdinalIgnoreCase));
		}

		var matches = query
			.OrderBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();
		var shown = command.Limit == 0 ? matches.Count : Math.Min(command.Limit, matches.Count);
		var parsed = 0;
		for (var index = 0; index < shown; index++)
		{
			var resource = matches[index];
			ElexScene scene;
			try
			{
				scene = ElexScene.Parse(resource.Name, resource.VirtualPath, ReadAsset(assets, resource));
				parsed++;
			}
			catch (Exception exception) when (
				exception is GenomeFormatException or IOException or UnauthorizedAccessException or
					AssetNotFoundException)
			{
				error.WriteLine($"Skipping navigation sector '{resource.Name}': {exception.Message}");
				continue;
			}

			var flexPaths = 0;
			var connectors = 0;
			var helpers = 0;
			foreach (var entity in scene.Entities)
			{
				var kind = GetNavigationKind(entity);
				if (kind is null)
				{
					continue;
				}

				if (kind == "flex")
				{
					flexPaths++;
				}
				else if (kind == "connector")
				{
					connectors++;
				}
				else
				{
					helpers++;
				}
			}

			output.WriteLine(
				$"{scene.Name}\t{flexPaths}\t{connectors}\t{helpers}\t" +
				$"{scene.Entities.Count}\t{scene.SourcePath}");
			if (!command.ShowProperties)
			{
				continue;
			}

			foreach (var entity in scene.Entities)
			{
				var kind = GetNavigationKind(entity);
				if (kind is null)
				{
					continue;
				}

				var position = entity.WorldPosition;
				output.WriteLine(
					$"\tnav\t{kind}\t{entity.Name}\t" +
					$"{position.X:0.###},{position.Y:0.###},{position.Z:0.###}");
			}
		}

		error.WriteLine($"Showing {shown} of {matches.Count:N0} navigation sector(s); parsed {parsed:N0}.");
		return 0;
	}

	private static string? GetNavigationKind(ElexSceneEntity entity)
	{
		foreach (var propertySet in entity.PropertySets)
		{
			var name = GenomeNameTable.GetNameOrHash(propertySet.ClassHash);
			if (string.Equals(name, "class gCNavFlexPath_PS", StringComparison.Ordinal))
			{
				return "flex";
			}

			if (string.Equals(name, "class gCNavOffMeshConnector_PS", StringComparison.Ordinal))
			{
				return "connector";
			}

			if (string.Equals(name, "class gCAIHelper_FreePoint_PS", StringComparison.Ordinal))
			{
				return "helper";
			}
		}

		return null;
	}

	private static int Sounds(
		AssetCommandLine command,
		AssetCatalog catalog,
		AssetFileSystem assets,
		TextWriter output,
		TextWriter error)
	{
		var resources = ElexResourceIndex.Build(catalog, assets);
		foreach (var diagnostic in resources.Diagnostics.Take(5))
		{
			error.WriteLine($"Sound warning: {diagnostic}");
		}

		IEnumerable<ElexResource> query = resources.Resources.Where(resource =>
			string.Equals(resource.TypeToken, "snd", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(resource.TypeToken, "dlg", StringComparison.OrdinalIgnoreCase));
		if (!string.IsNullOrEmpty(command.Filter))
		{
			query = query.Where(resource =>
				resource.Name.Contains(command.Filter, StringComparison.OrdinalIgnoreCase));
		}

		var matches = query
			.OrderBy(resource => resource.TypeToken, StringComparer.OrdinalIgnoreCase)
			.ThenBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();
		var shown = command.Limit == 0 ? matches.Count : Math.Min(command.Limit, matches.Count);
		var parsed = 0;
		for (var index = 0; index < shown; index++)
		{
			var resource = matches[index];
			FsbSoundBank bank;
			try
			{
				bank = FsbSoundBank.Parse(
					resource.Name,
					resource.VirtualPath,
					resource.TypeToken,
					ReadAsset(assets, resource));
				parsed++;
			}
			catch (Exception exception) when (
				exception is GenomeFormatException or IOException or UnauthorizedAccessException or
					AssetNotFoundException)
			{
				error.WriteLine($"Skipping sound bank '{resource.Name}': {exception.Message}");
				continue;
			}

			var first = bank.Samples[0];
			var duration = bank.Samples.Sum(sample => sample.Duration);
			output.WriteLine(
				$"{bank.Name}\t{bank.TypeToken}\t{bank.Version}\t{bank.CodecName}\t" +
				$"{bank.Samples.Count}\t{first.Channels}\t{first.SampleRate}\t" +
				$"{duration:0.###}\t{bank.SampleDataSize}\t{bank.SourcePath}");
			if (!command.ShowProperties)
			{
				continue;
			}

			foreach (var sample in bank.Samples)
			{
				var loop = sample.HasLoop ?
					$"{sample.LoopStart}-{sample.LoopEnd}" :
					"-";
				output.WriteLine(
					$"\tsample\t{sample.Index}\t{sample.Name ?? "-"}\t{sample.Channels}\t" +
					$"{sample.SampleRate}\t{sample.SampleCount}\t{sample.Duration:0.###}\t" +
					$"{sample.DataSize}\t{loop}");
			}
		}

		error.WriteLine($"Showing {shown} of {matches.Count:N0} sound bank(s); parsed {parsed:N0}.");
		return 0;
	}

	private static int Strings(
		AssetCommandLine command,
		AssetCatalog catalog,
		AssetFileSystem assets,
		TextWriter output,
		TextWriter error)
	{
		ElexLocalizationConfig? localization = null;
		if (catalog.TryGetEntry("ini/loc.ini", out var locEntry))
		{
			try
			{
				localization = ElexLocalizationConfig.Parse(ReadAsset(assets, locEntry.VirtualPath));
			}
			catch (Exception exception) when (
				exception is GenomeFormatException or IOException or UnauthorizedAccessException or
					AssetNotFoundException)
			{
				error.WriteLine($"Localization warning: {exception.Message}");
			}
		}

		var entries = catalog.Entries
			.Where(entry => string.Equals(entry.Extension, ".str", StringComparison.OrdinalIgnoreCase))
			.OrderBy(entry => entry.VirtualPath, StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (entries.Count == 0)
		{
			error.WriteLine("No compiled string tables were found.");
			return 0;
		}

		var filter = command.Filter;
		var limit = command.Limit;
		var showAll = limit == 0;
		var isHash = filter is not null && TryParseHash(filter, out _);
		var parsed = 0;
		foreach (var entry in entries)
		{
			ElexStringTable table;
			try
			{
				table = ElexStringTable.Parse(
					entry.VirtualPath,
					entry.VirtualPath,
					ReadAsset(assets, entry.VirtualPath));
				parsed++;
			}
			catch (Exception exception) when (
				exception is GenomeFormatException or IOException or UnauthorizedAccessException or
					AssetNotFoundException)
			{
				error.WriteLine($"Skipping string table '{entry.VirtualPath}': {exception.Message}");
				continue;
			}

			output.WriteLine(
				$"{table.Name}\tversion={table.Version}\tsources={table.Sources.Count}\t" +
				$"columns={table.Columns.Count}\trows={table.RowCount}\t{entry.SourceName}");
			if (localization is not null)
			{
				output.WriteLine($"\tloc\t{localization.Entries.Count}\t{string.Join(',', localization.Entries.Take(8).Select(item => item.Prefix))}");
			}

			if (!string.IsNullOrEmpty(filter))
			{
				if (isHash && TryParseHash(filter, out var hash))
				{
					foreach (var column in table.Columns)
					{
						if (table.TryGet(hash, column.Name, out var text))
						{
							output.WriteLine(
								$"\t{hash:x8}\t{column.Name}\t{Escape(text)}");
						}
					}
				}
				else
				{
					foreach (var match in table.Find(filter, showAll ? int.MaxValue : Math.Max(1, limit)))
					{
						output.WriteLine(
							$"\t{match.IdHash:x8}\t{match.Column}\t{Escape(match.Text)}");
					}
				}

				continue;
			}

			if (!command.ShowProperties)
			{
				continue;
			}

			var rowLimit = showAll ? table.IdHashes.Count : Math.Min(Math.Max(1, limit), table.IdHashes.Count);
			for (var row = 0; row < rowLimit; row++)
			{
				var hash = table.IdHashes[row];
				foreach (var column in table.Columns)
				{
					if (table.TryGet(hash, column.Name, out var text))
					{
						output.WriteLine(
							$"\t{hash:x8}\t{column.Name}\t{Escape(text)}");
					}
				}
			}
		}

		error.WriteLine($"Showing {parsed:N0} of {entries.Count:N0} string table(s).");
		return 0;
	}

	private static bool TryParseHash(string value, out uint hash)
	{
		hash = 0;
		var text = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
		return text.Length is > 0 and <= 8 &&
			uint.TryParse(
				text,
				System.Globalization.NumberStyles.HexNumber,
				System.Globalization.CultureInfo.InvariantCulture,
				out hash);
	}

	private static string Escape(string value) =>
		value.Replace("\r", "\\r", StringComparison.Ordinal)
			.Replace("\n", "\\n", StringComparison.Ordinal)
			.Replace("\t", "\\t", StringComparison.Ordinal);

	private static byte[] ReadAsset(AssetFileSystem assets, ElexResource resource) =>
		ReadAsset(assets, resource.VirtualPath);

	private static byte[] ReadAsset(AssetFileSystem assets, string virtualPath)
	{
		using var stream = assets.OpenRead(virtualPath);
		using var buffer = new MemoryStream();
		stream.CopyTo(buffer);
		return buffer.ToArray();
	}

	private static int Textures(
		AssetCommandLine command,
		AssetCatalog catalog,
		AssetFileSystem assets,
		TextWriter output,
		TextWriter error)
	{
		var resources = ElexResourceIndex.Build(catalog, assets);
		foreach (var diagnostic in resources.Diagnostics.Take(5))
		{
			error.WriteLine($"Texture warning: {diagnostic}");
		}

		IEnumerable<ElexResource> query = resources.Resources.Where(resource =>
			string.Equals(resource.TypeToken, "img", StringComparison.OrdinalIgnoreCase));
		if (!string.IsNullOrEmpty(command.Filter))
		{
			query = query.Where(resource =>
				resource.Name.Contains(command.Filter, StringComparison.OrdinalIgnoreCase));
		}

		var matches = query
			.OrderBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();
		var shown = command.Limit == 0 ? matches.Count : Math.Min(command.Limit, matches.Count);
		var parsed = 0;
		var decodeFailures = 0;
		for (var index = 0; index < shown; index++)
		{
			var resource = matches[index];
			DdsImage image;
			try
			{
				using var stream = assets.OpenRead(resource.VirtualPath);
				using var buffer = new MemoryStream();
				stream.CopyTo(buffer);
				image = DdsImage.Parse(buffer.ToArray());
				parsed++;
			}
			catch (Exception exception) when (
				exception is DdsFormatException or IOException or UnauthorizedAccessException or
					AssetNotFoundException)
			{
				error.WriteLine($"Skipping texture '{resource.Name}': {exception.Message}");
				continue;
			}

			output.WriteLine(
				$"{resource.Name}\t{image.Width}\t{image.Height}\t{image.MipCount}\t" +
				$"{image.FormatName}\t{image.IsSrgb}\t{image.IsVolume}\t{image.IsCubemap}\t" +
				$"{image.ArraySize}\t{resource.Size}\t{resource.VirtualPath}");

			if (command.ShowProperties)
			{
				output.WriteLine(
					$"\tddsOffset={image.DdsOffset}\tdataOffset={image.DataOffset}\t" +
					$"dataLength={image.DataLength}\ttrailing={image.TrailingLength}");
				output.WriteLine(
					$"\tfourCc={image.FourCcText}\tdxgi={image.DxgiFormat?.ToString() ?? ""}\t" +
					$"flags=0x{image.Flags:x8}\tcaps=0x{image.Caps:x8}\tcaps2=0x{image.Caps2:x8}");
				output.WriteLine(
					$"\tpixelFlags=0x{image.PixelFormatFlags:x8}\tbits={image.RgbBitCount}\t" +
					$"masks={image.RedMask:x8}/{image.GreenMask:x8}/{image.BlueMask:x8}/{image.AlphaMask:x8}");
				for (var face = 0; face < image.Faces.Count; face++)
				{
					for (var mip = 0; mip < image.Faces[face].Mipmaps.Count; mip++)
					{
						var level = image.Faces[face].Mipmaps[mip];
						output.WriteLine(
							$"\tface={face}\tmip={mip}\t{level.Width}x{level.Height}x{level.Depth}\t" +
							$"bytes={level.Data.Length}");
					}
				}
			}

			if (command.Decode)
			{
				try
				{
					var normalMap = resource.Name.Contains("_Nm", StringComparison.OrdinalIgnoreCase);
					var level = DdsTextureDecoder.Decode(
						image,
						0,
						new DdsDecodeOptions(NormalMap: normalMap));
					output.WriteLine(
						$"\tdecoded\t{level.Width}x{level.Height}\tchecksum={Checksum(level.Pixels):x8}");
				}
				catch (Exception exception) when (
					exception is DdsFormatException or NotSupportedException or ArgumentException)
				{
					decodeFailures++;
					error.WriteLine($"Could not decode texture '{resource.Name}': {exception.Message}");
				}
			}
		}

		error.WriteLine(
			$"Showing {shown} of {matches.Count:N0} texture(s); parsed {parsed:N0}" +
			(command.Decode ? $", decode failures {decodeFailures:N0}" : string.Empty) + ".");
		return 0;
	}

	private static int Meshes(
		AssetCommandLine command,
		AssetCatalog catalog,
		AssetFileSystem assets,
		TextWriter output,
		TextWriter error)
	{
		var resources = ElexResourceIndex.Build(catalog, assets);
		foreach (var diagnostic in resources.Diagnostics.Take(5))
		{
			error.WriteLine($"Mesh warning: {diagnostic}");
		}

		IEnumerable<ElexResource> query = resources.Resources.Where(resource =>
			string.Equals(resource.TypeToken, "msh", StringComparison.OrdinalIgnoreCase));
		if (!string.IsNullOrEmpty(command.Filter))
		{
			query = query.Where(resource =>
				resource.Name.Contains(command.Filter, StringComparison.OrdinalIgnoreCase));
		}

		var matches = query
			.OrderBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();
		var shown = command.Limit == 0 ? matches.Count : Math.Min(command.Limit, matches.Count);
		var parsed = 0;
		using var classBlocks = new ResourceClassBlockReader(assets);
		for (var index = 0; index < shown; index++)
		{
			var resource = matches[index];
			GmshMesh mesh;
			try
			{
				using var stream = assets.OpenRead(resource.VirtualPath);
				using var buffer = new MemoryStream();
				stream.CopyTo(buffer);
				mesh = GmshMesh.Parse(resource.Name, resource.VirtualPath, buffer.ToArray());
				parsed++;
			}
			catch (Exception exception) when (
				exception is GmshFormatException or IOException or UnauthorizedAccessException or
					AssetNotFoundException)
			{
				error.WriteLine($"Skipping mesh '{resource.Name}': {exception.Message}");
				continue;
			}

			var min = mesh.Positions[0];
			var max = mesh.Positions[0];
			foreach (var position in mesh.Positions)
			{
				min = System.Numerics.Vector3.Min(min, position);
				max = System.Numerics.Vector3.Max(max, position);
			}

			output.WriteLine(
				$"{resource.Name}\t{mesh.Positions.Count}\t{mesh.TriangleCount}\t" +
				$"{mesh.VertexAttributes.Count}\t{mesh.VertexStreams.Count}\t" +
				$"{mesh.BufferDescriptors.Count}\t" +
				$"{min.X:0.###},{min.Y:0.###},{min.Z:0.###}\t" +
				$"{max.X:0.###},{max.Y:0.###},{max.Z:0.###}\t{resource.VirtualPath}");

			if (command.ShowProperties)
			{
				output.WriteLine(
					$"\tgmshOffset={mesh.GmshOffset}\tversion={mesh.Version}\t" +
					$"indexOffset={mesh.IndexOffset}\tindices={mesh.Indices.Count}\t" +
					$"isTriangleList={mesh.IsTriangleList}\tindexElementSize=" +
					$"{(mesh.BufferDescriptors.Count > 0 ? mesh.BufferDescriptors[0].ElementSize : 0)}");
				output.WriteLine(
					$"\tlayout\tindexDataOffset=0x{mesh.IndexDataOffset:X}\t" +
					$"vertexDataOffset=0x{mesh.VertexDataOffset:X}\t" +
					$"vertexDataLength={mesh.VertexDataLength}\t" +
					$"hasNormals={mesh.HasNormals}\thasTextureCoordinates={mesh.HasTextureCoordinates}\t" +
					$"hasTangents={mesh.Tangents.Count > 0}\thasColors={mesh.Colors.Count > 0}\t" +
					$"hasTextureCoordinates1={mesh.TextureCoordinates1.Count > 0}");
				if (mesh.Normals.Count == mesh.Positions.Count && mesh.Normals.Count > 0)
				{
					var winding = MeshWinding.Analyze(mesh.Positions, mesh.Normals, mesh.Indices);
					output.WriteLine(
						$"\twinding\tagree={winding.Agreeing}\tdisagree={winding.Disagreeing}\t" +
						$"degenerate={winding.Degenerate}\tratio={winding.AgreementRatio:0.####}");
				}
				else
				{
					output.WriteLine("\twinding\tn/a");
				}

				var deviation = MaxSourcePositionDeviation(mesh);
				output.WriteLine($"\tpositions\tmaxSourceDeviation={deviation:0.######}");
				foreach (var attribute in mesh.VertexAttributes)
				{
					output.WriteLine(
						$"\tattribute\t{attribute.UsageName}\toffset={attribute.UsageOffset}\t" +
						$"format={attribute.Format}\tformatName={attribute.FormatName}\t" +
						$"slot={attribute.Slot}\t" +
						$"unknown={attribute.Unknown0:x8}/{attribute.Unknown1:x8}/{attribute.Unknown2:x8}");
				}

				foreach (var stream in mesh.VertexStreams)
				{
					output.WriteLine(
						$"\tstream\t{stream.UsageName}\toffset={stream.Offset}\t" +
						$"stride={stream.Stride}\tunknown={stream.Unknown}");
				}

				for (var descriptor = 0; descriptor < mesh.BufferDescriptors.Count; descriptor++)
				{
					var buffer = mesh.BufferDescriptors[descriptor];
					output.WriteLine(
						$"\tbuffer\t{descriptor}\tsize={buffer.Size}\telementSize={buffer.ElementSize}\t" +
						$"hash={buffer.Hash:x8}\t" +
						string.Join('/', buffer.Raw.Select(value => value.ToString("x8"))));
				}

				try
				{
					var metadata = MeshMetadata.Parse(classBlocks.Read(resource));
					output.WriteLine(
						$"\tmetadata\tindexCount={metadata.IndexCount}\tvertexCount={metadata.VertexCount}\t" +
						$"lodStart={metadata.LODRangeStart:0.###}\tsubMeshes={metadata.SubMeshes.Count}");
					foreach (var subMesh in metadata.SubMeshes)
					{
						output.WriteLine(
							$"\tsubmesh\t{subMesh.MaterialName}\tfirstIndex={subMesh.FirstIndex}\t" +
							$"indexCount={subMesh.IndexCount}\tfirstVertex={subMesh.FirstVertex}\t" +
							$"vertexCount={subMesh.VertexCount}\tfirstBone={subMesh.FirstBone}\t" +
							$"boneCount={subMesh.BoneCount}\tmaterials={subMesh.MaterialSwitchIndex}");
					}
				}
				catch (Exception exception) when (
					exception is Gar5FormatException or GenomeFormatException or IOException or
						UnauthorizedAccessException or AssetNotFoundException)
				{
					error.WriteLine($"Skipping mesh metadata '{resource.Name}': {exception.Message}");
				}

				foreach (var diagnostic in mesh.Diagnostics)
				{
					output.WriteLine($"\tdiagnostic\t{diagnostic}");
				}
			}
		}

		error.WriteLine(
			$"Showing {shown} of {matches.Count:N0} mesh(es); parsed {parsed:N0}.");
		return 0;
	}

	private static int Animations(
		AssetCommandLine command,
		AssetCatalog catalog,
		AssetFileSystem assets,
		TextWriter output,
		TextWriter error)
	{
		return InspectResources(
			command, catalog, assets, output, error, "net", "motion network",
			(resource, data) => MotionNetwork.Parse(resource.Name, data),
			(resource, network, showProperties) =>
			{
				output.WriteLine(
					$"{resource.Name}\t{network.Strings.Count}\t{network.BoneNames.Count}\t" +
					$"{network.Clips.Count}\t{network.Clips.Count(clip => clip.HasPose)}\t" +
					$"{network.Transitions.Count}\t{network.StateNames.Count}\t" +
					$"{network.StatePaths.Count}\t{network.GraphNodes.Count}\t" +
					$"{resource.VirtualPath}");
				if (showProperties)
				{
					foreach (var value in network.Strings)
					{
						output.WriteLine($"\tstring\t{value}");
					}

					foreach (var clip in network.Clips)
					{
						output.WriteLine(
							$"\tclip\t{clip.Index}\ttracks={clip.TrackNames.Count}\t" +
							$"field0={clip.Field0:G9}\tfield1={clip.Field1:G9}\t" +
							$"pose={clip.PoseOffset}\t{string.Join(',', clip.TrackNames.Take(4))}");
						for (var track = 0; track < Math.Min(4, clip.TrackNames.Count); track++)
						{
							if (!clip.HasPose)
							{
								break;
							}

							var value = clip.Pose[track];
							output.WriteLine(
								$"\t\ttrack\t{clip.TrackNames[track]}\t" +
								$"{value.X:F5},{value.Y:F5},{value.Z:F5},{value.W:F5}");
						}
					}

					foreach (var transition in network.Transitions)
					{
						output.WriteLine(
							$"\ttransition\t{transition.Source}\t{transition.Target}\t" +
							$"{transition.Condition}\t{transition.Name}");
					}

					if (network.StateNames.Count > 0)
					{
						output.WriteLine($"\tstates\t{string.Join(',', network.StateNames)}");
					}

					if (network.NodeNames.Count > 0)
					{
						output.WriteLine(
							$"\tnodes\t{string.Join(',', network.NodeNames.Take(16))}");
					}

					foreach (var path in network.StatePaths.Take(16))
					{
						output.WriteLine(
							$"\tpath\t{string.Join('|', path.Segments)}\t" +
							$"{path.Group}\t{path.Condition}");
					}

					foreach (var node in network.GraphNodes.Take(16))
					{
						output.WriteLine(
							$"\tgraph\t{node.Offset}\ttype={node.Type}\tsize={node.Size}\t" +
							$"count={node.Count}\trefs={string.Join(',', node.References.Take(12))}");
					}

					if (network.QuantizedQuaternionRuns.Count > 0)
					{
						output.WriteLine(
							$"\tquantized\t{network.QuantizedQuaternionRuns.Count}\t" +
							$"{string.Join(',', network.QuantizedQuaternionRuns.Take(8).Select(run => $"{run.Offset}:{run.Count}"))}");
					}

					if (network.SampleTracks.Count > 0)
					{
						output.WriteLine(
							$"\tsamples\t{network.SampleTracks.Count}\t" +
							$"{string.Join(',', network.SampleTracks.Take(8).Select(track => $"{track.TrackIndex}/{track.BoneIndex}:{track.SampleCount}@{track.Duration:0.##}"))}");
					}

					if (network.SampleGroups.Count > 0)
					{
						output.WriteLine(
							$"\tgroups\t{network.SampleGroups.Count}\t" +
							$"{string.Join(',', network.SampleGroups.Take(8).Select(group => $"{group.Duration:0.###}:{group.Tracks.Count}"))}");
					}

					foreach (var state in network.GraphStates)
					{
						output.WriteLine(
							$"\tstate\t{state.Name ?? "-"}\tblock={state.BlockOffset}\t" +
							$"index={state.Index}\tvalues={state.Values.Count}\t" +
							$"refs={state.References.Count}\t" +
							$"first={string.Join(',', state.Values.Take(4).Select(record => record[0]))}");
					}

					if (network.GraphTransitions.Count > 0)
					{
						output.WriteLine(
							$"\ttimed\t{network.GraphTransitions.Count}\t" +
							$"{string.Join(',', network.GraphTransitions.Take(8).Select(transition => $"{transition.Index}:{transition.Source}->{transition.Target}@{transition.DurationMilliseconds}ms"))}");
					}
				}
			});
	}

	private static int BlendShapes(
		AssetCommandLine command,
		AssetCatalog catalog,
		AssetFileSystem assets,
		TextWriter output,
		TextWriter error)
	{
		return InspectResources(
			command, catalog, assets, output, error, "bln", "blend-shape set",
			(resource, data) => ElexBlendShapeSet.Parse(resource.Name, data),
			(resource, set, showProperties) =>
			{
				output.WriteLine(
					$"{resource.Name}\t{set.TargetCount}\t{set.VertexCount}\t{set.Names.Count}\t" +
					$"{set.Field0}\t{set.Field1}\t{set.Field2}\t" +
					$"{set.Targets.Sum(target => target.Deltas.Count)}\t{set.DeltasDecoded}\t" +
					$"{string.Join(',', set.Names.Take(4))}\t{resource.VirtualPath}");
				if (showProperties)
				{
					for (var index = 0; index < set.Names.Count; index++)
					{
						var target = set.Targets.Count > index ? set.Targets[index] : null;
						output.WriteLine(
							$"\ttarget\t{index}\toffset={(index == 0 ? 0 : set.NameOffsets[index - 1])}\t" +
							$"deltas={target?.Deltas.Count ?? 0}\t{set.Names[index]}");
						if (target is null)
						{
							continue;
						}

						foreach (var delta in target.Deltas.Take(2))
						{
							output.WriteLine(
								$"\t\tdelta\tvertex={delta.VertexIndex}\t" +
								$"position={delta.Position.X:F4},{delta.Position.Y:F4},{delta.Position.Z:F4}\t" +
								$"normal={delta.Normal.X:F4},{delta.Normal.Y:F4},{delta.Normal.Z:F4}");
						}
					}
				}
			});
	}

	private static int InspectResources<T>(
		AssetCommandLine command,
		AssetCatalog catalog,
		AssetFileSystem assets,
		TextWriter output,
		TextWriter error,
		string typeToken,
		string label,
		Func<ElexResource, byte[], T> parse,
		Action<ElexResource, T, bool> write)
	{
		var resources = ElexResourceIndex.Build(catalog, assets);
		foreach (var diagnostic in resources.Diagnostics.Take(5))
		{
			error.WriteLine($"{label} warning: {diagnostic}");
		}

		IEnumerable<ElexResource> query = resources.Resources.Where(resource =>
			string.Equals(resource.TypeToken, typeToken, StringComparison.OrdinalIgnoreCase));
		if (!string.IsNullOrEmpty(command.Filter))
		{
			query = query.Where(resource =>
				resource.Name.Contains(command.Filter, StringComparison.OrdinalIgnoreCase));
		}

		var matches = query
			.OrderBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();
		var shown = command.Limit == 0 ? matches.Count : Math.Min(command.Limit, matches.Count);
		var parsed = 0;
		for (var index = 0; index < shown; index++)
		{
			var resource = matches[index];
			try
			{
				var data = ReadAll(assets, resource.VirtualPath);
				var value = parse(resource, data);
				parsed++;
				write(resource, value, command.ShowProperties);
			}
			catch (Exception exception) when (
				exception is Gar5FormatException or IOException or UnauthorizedAccessException or
					AssetNotFoundException or InvalidOperationException)
			{
				error.WriteLine($"Skipping {label} '{resource.Name}': {exception.Message}");
			}
		}

		error.WriteLine($"Showing {shown} of {matches.Count:N0} {label}(s); parsed {parsed:N0}.");
		return 0;
	}

	private static int Skeletons(
		AssetCommandLine command,
		AssetCatalog catalog,
		AssetFileSystem assets,
		TextWriter output,
		TextWriter error)
	{
		var resources = ElexResourceIndex.Build(catalog, assets);
		foreach (var diagnostic in resources.Diagnostics.Take(5))
		{
			error.WriteLine($"Skeleton warning: {diagnostic}");
		}

		IEnumerable<ElexResource> query = resources.Resources.Where(resource =>
			string.Equals(resource.TypeToken, "skn", StringComparison.OrdinalIgnoreCase));
		if (!string.IsNullOrEmpty(command.Filter))
		{
			query = query.Where(resource =>
				resource.Name.Contains(command.Filter, StringComparison.OrdinalIgnoreCase));
		}

		var matches = query
			.OrderBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();
		var shown = command.Limit == 0 ? matches.Count : Math.Min(command.Limit, matches.Count);
		var parsed = 0;
		var maxBones = 0;
		using var classBlocks = new ResourceClassBlockReader(assets);
		for (var index = 0; index < shown; index++)
		{
			var resource = matches[index];
			byte[] data;
			try
			{
				data = ReadAll(assets, resource.VirtualPath);
			}
			catch (Exception exception) when (
				exception is IOException or UnauthorizedAccessException or AssetNotFoundException)
			{
				error.WriteLine($"Skipping skeleton '{resource.Name}': {exception.Message}");
				continue;
			}

			var gmshOffset = data.AsSpan().IndexOf("GMSH"u8);
			if (gmshOffset < 0 || gmshOffset + 12 > data.Length)
			{
				error.WriteLine($"Skipping skeleton '{resource.Name}': no GMSH block.");
				continue;
			}

			var tailOffset = gmshOffset + (int)BitConverter.ToUInt32(data, gmshOffset + 8);
			var skeleton = Skeleton.TryParse(data, tailOffset);
			if (skeleton is null)
			{
				error.WriteLine($"Skipping skeleton '{resource.Name}': unrecognized skeleton tail.");
				continue;
			}

			parsed++;
			maxBones = Math.Max(maxBones, skeleton.BoneCount);
			var vertexCount = 0;
			var indexCount = 0;
			try
			{
				var metadata = MeshMetadata.Parse(classBlocks.Read(resource));
				vertexCount = metadata.VertexCount;
				indexCount = metadata.IndexCount;
			}
			catch (Exception exception) when (
				exception is Gar5FormatException or GenomeFormatException or IOException or
					UnauthorizedAccessException or AssetNotFoundException)
			{
				error.WriteLine($"Skipping skeleton metadata '{resource.Name}': {exception.Message}");
			}

			output.WriteLine(
				$"{resource.Name}\t{vertexCount}\t{indexCount}\t{skeleton.BoneCount}\t" +
				$"{skeleton.Names.Count}\t{skeleton.Bones.Count}\t" +
				$"{string.Join(',', skeleton.Names)}\t{resource.VirtualPath}");
			if (command.ShowProperties)
			{
				output.WriteLine(
					$"\theader\t{string.Join(',', skeleton.Header.Select(value => value.ToString("0.###")))}");
				foreach (var bone in skeleton.Bones)
				{
					output.WriteLine(
						$"\tbone\t{bone.Index}\tposition={bone.Position.X:0.###},{bone.Position.Y:0.###}," +
						$"{bone.Position.Z:0.###}\trotation={bone.Rotation.X:0.###},{bone.Rotation.Y:0.###}," +
						$"{bone.Rotation.Z:0.###},{bone.Rotation.W:0.###}");
				}

				for (var nameIndex = 0; nameIndex < skeleton.Names.Count; nameIndex++)
				{
					output.WriteLine($"\tname\t{skeleton.NameHashes[nameIndex]:x8}\t{skeleton.Names[nameIndex]}");
				}

				if (skeleton.ParentIndices.Count > 0)
				{
					output.WriteLine(
						$"\tparents\t{string.Join('/', skeleton.ParentIndices)}");
				}

				if (skeleton.NameOffsets.Count > 0)
				{
					output.WriteLine(
						$"\toffsets\t{string.Join('/', skeleton.NameOffsets)}");
				}
			}
		}

		error.WriteLine(
			$"Showing {shown} of {matches.Count:N0} skeleton(s); parsed {parsed:N0}; max bones {maxBones}.");
		return 0;
	}

	private static int Materials(
		AssetCommandLine command,
		AssetCatalog catalog,
		AssetFileSystem assets,
		TextWriter output,
		TextWriter error)
	{
		var resources = ElexResourceIndex.Build(catalog, assets);
		foreach (var diagnostic in resources.Diagnostics.Take(5))
		{
			error.WriteLine($"Material warning: {diagnostic}");
		}

		IEnumerable<ElexResource> query = resources.Resources.Where(resource =>
			string.Equals(resource.TypeToken, "mat", StringComparison.OrdinalIgnoreCase));
		if (!string.IsNullOrEmpty(command.Filter))
		{
			query = query.Where(resource =>
				resource.Name.Contains(command.Filter, StringComparison.OrdinalIgnoreCase));
		}

		var matches = query
			.OrderBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();
		var shown = command.Limit == 0 ? matches.Count : Math.Min(command.Limit, matches.Count);
		var parsed = 0;
		using var classBlocks = new ResourceClassBlockReader(assets);
		for (var index = 0; index < shown; index++)
		{
			var resource = matches[index];
			ElexMaterial material;
			try
			{
				using var stream = assets.OpenRead(resource.VirtualPath);
				using var buffer = new MemoryStream();
				stream.CopyTo(buffer);
				material = ElexMaterial.Parse(resource.Name, buffer.ToArray());
				parsed++;
			}
			catch (Exception exception) when (
				exception is Gar5FormatException or IOException or UnauthorizedAccessException or
					AssetNotFoundException)
			{
				error.WriteLine($"Skipping material '{resource.Name}': {exception.Message}");
				continue;
			}

			if (command.ShowProperties)
			{
				try
				{
					material.Shader = MaterialMetadata.Parse(classBlocks.Read(resource)).InstanceMaterial;
				}
				catch (Exception exception) when (
					exception is Gar5FormatException or GenomeFormatException or IOException or
						UnauthorizedAccessException or AssetNotFoundException)
				{
					error.WriteLine($"Skipping material metadata '{resource.Name}': {exception.Message}");
				}
			}

			output.WriteLine(
				$"{resource.Name}\t{material.Textures.Count}\t{material.TemplateName}\t" +
				$"{material.Shader ?? string.Empty}\t" +
				$"{material.PrimaryTexture?.Name ?? string.Empty}\t{resource.VirtualPath}");
			if (command.ShowProperties)
			{
				foreach (var texture in material.Textures)
				{
					output.WriteLine($"\ttexture\t{texture.Usage}\t{texture.Name}");
				}
			}
		}

		error.WriteLine(
			$"Showing {shown} of {matches.Count:N0} material(s); parsed {parsed:N0}.");
		return 0;
	}

	private static float MaxSourcePositionDeviation(GmshMesh mesh)
	{
		var count = Math.Min(mesh.Positions.Count, mesh.SourcePositions.Count);
		var max = 0f;
		for (var index = 0; index < count; index++)
		{
			max = Math.Max(
				max,
				System.Numerics.Vector3.Distance(mesh.Positions[index], mesh.SourcePositions[index]));
		}

		return max;
	}

	private static byte[] ReadAll(AssetFileSystem assets, string virtualPath)
	{
		using var stream = assets.OpenRead(virtualPath);
		using var buffer = new MemoryStream();
		stream.CopyTo(buffer);
		return buffer.ToArray();
	}

	private static uint Checksum(byte[] data)
	{
		var hash = 2166136261u;
		foreach (var value in data)
		{
			hash ^= value;
			hash *= 16777619u;
		}

		return hash;
	}

	private static int Export(
		AssetCommandLine command,
		AssetCatalog catalog,
		AssetFileSystem assets,
		TextWriter output,
		TextWriter error)
	{
		if (command.AssetPath is null || command.DestinationPath is null)
		{
			error.WriteLine("export requires an asset path and a destination.");
			return 3;
		}

		if (!catalog.TryGetEntry(command.AssetPath, out var entry))
		{
			error.WriteLine($"Asset '{command.AssetPath}' was not found.");
			return 3;
		}

		if (entry.IsExecutable)
		{
			error.WriteLine($"Refusing to export executable file '{entry.VirtualPath}'.");
			return 3;
		}

		try
		{
			var destinationPath = Path.GetFullPath(command.DestinationPath);
			var directory = Path.GetDirectoryName(destinationPath);
			if (!string.IsNullOrEmpty(directory))
			{
				Directory.CreateDirectory(directory);
			}

			using var source = assets.OpenRead(entry.VirtualPath);
			using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
			source.CopyTo(destination);
			if (destination.Length != entry.Length)
			{
				throw new IOException(
					$"Exported {destination.Length} bytes but the catalog reported {entry.Length}.");
			}

			output.WriteLine($"Exported {entry.VirtualPath} -> {destinationPath} ({FormatBytes(entry.Length)}).");
			return 0;
		}
		catch (Exception exception) when (
			exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
				or AssetNotFoundException)
		{
			error.WriteLine(exception.Message);
			return 3;
		}
	}

	private static void WriteGroups(TextWriter output, string title, IEnumerable<AssetGroupStatistics> groups)
	{
		output.WriteLine(title);
		foreach (var group in groups)
		{
			output.WriteLine($"  {group.Count,8:N0}  {FormatBytes(group.Bytes),12}  {group.Name}");
		}
	}

	private static string FormatBytes(long bytes)
	{
		const double Kilobyte = 1024;
		const double Megabyte = Kilobyte * 1024;
		const double Gigabyte = Megabyte * 1024;

		return bytes switch
		{
			< 1024 => $"{bytes} B",
			< 1024 * 1024 => $"{bytes / Kilobyte:F1} KB",
			< 1024L * 1024 * 1024 => $"{bytes / Megabyte:F1} MB",
			_ => $"{bytes / Gigabyte:F1} GB",
		};
	}
}
