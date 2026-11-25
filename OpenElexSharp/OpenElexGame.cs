using DigitalRiseModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Myra;
using Myra.Graphics2D.UI;
using OpenElexSharp.Assets;
using OpenElexSharp.Diagnostics;
using OpenElexSharp.Graphics;
using OpenElexSharp.UI;
using System.Diagnostics;

namespace OpenElexSharp;

/// <summary>
/// The MonoGame application. For now it opens a window and displays one game texture or one
/// static mesh in a test scene. World loading and gameplay systems are added by later tasks.
/// </summary>
public sealed class OpenElexGame : Game
{
	private const int SmokeTestFrameCount = 10;
	private const int TextureMargin = 24;
	private const double StatisticsInterval = 0.5;
	private static readonly TimeSpan SoakSampleInterval = TimeSpan.FromSeconds(10);

	private readonly GraphicsDeviceManager _graphics;
	private readonly LaunchOptions _options;
	private readonly FrameStatistics _frameStatistics = new();
	private int _updateCount;
	private int _frameCount;
	private bool _captured;
	private bool _deviceLost;
	private bool _resizing;
	private FrameSnapshot _statistics;
	private double _statisticsElapsed;
	private bool _statisticsWarmedUp;
	private double _lastUpdateMilliseconds;
	private double _lastDrawMilliseconds;
	private TimeSpan _soakElapsed;
	private TimeSpan _soakNextSample;
	private Renderer? _renderer;
	private Desktop? _desktop;
	private Hud? _hud;
	private Texture2D? _texture;
	private Texture2D? _fallback;
	private DrMesh? _mesh;
	private ElexCollisionMesh? _collision;
	private VertexPositionColor[]? _collisionLines;
	private MaterialLibrary? _materials;
	private Skeleton? _skeleton;
	private MotionNetwork? _network;
	private MotionClip? _clipA;
	private MotionClip? _clipB;
	private MotionClip? _sampleClip;
	private MotionSampleGroup? _sampleGroup;
	private KeyboardState _previousKeyboard;

	public OpenElexGame(LaunchOptions options, AssetFileSystem assets)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(assets);

		_options = options;
		Assets = assets;
		_graphics = new GraphicsDeviceManager(this)
		{
			PreferredBackBufferWidth = 1280,
			PreferredBackBufferHeight = 720,
			GraphicsProfile = GraphicsProfile.HiDef,
			SynchronizeWithVerticalRetrace = true,
		};

		IsFixedTimeStep = true;
		TargetElapsedTime = TimeSpan.FromSeconds(1d / 60d);
		InactiveSleepTime = TimeSpan.FromSeconds(0.1);
		_soakNextSample = SoakSampleInterval;

		Content.RootDirectory = "Content";
		IsMouseVisible = true;
		Window.AllowUserResizing = true;
		Window.Title = "OpenElexSharp";
		Window.ClientSizeChanged += (_, _) => ResizeBackBuffer();
		Window.KeyDown += (_, eventArgs) =>
		{
			if (eventArgs.Key == Keys.Escape)
			{
				Exit();
			}
		};
	}

	/// <summary>The mounted game assets.</summary>
	public AssetFileSystem Assets { get; }

	protected override void LoadContent()
	{
		_renderer = new Renderer(GraphicsDevice);
		GraphicsDevice.DeviceLost += OnDeviceLost;
		GraphicsDevice.DeviceResetting += (_, _) =>
			Log.Info("Graphics device is resetting (window resize).");
		GraphicsDevice.DeviceReset += (_, _) =>
			Log.Info("Graphics device reset complete.");
		MyraEnvironment.Game = this;
		_desktop = new Desktop();
		_hud = new Hud();
		_desktop.Root = _hud;
		if (!string.IsNullOrWhiteSpace(_options.Animation))
		{
			try
			{
				LoadAnimationScene();
			}
			catch (Exception exception) when (
				exception is GmshFormatException or GenomeFormatException or Gar5FormatException or
					NotSupportedException or IOException or UnauthorizedAccessException or
					AssetNotFoundException or InvalidOperationException or ArgumentException)
			{
				Log.Warning($"Animation warning: {exception.Message}");
			}

			return;
		}

		if (!string.IsNullOrWhiteSpace(_options.Mesh))
		{
			try
			{
				_mesh = LoadMesh(_options.Mesh);
			}
			catch (Exception exception) when (
				exception is GmshFormatException or GenomeFormatException or Gar5FormatException or
					NotSupportedException or IOException or UnauthorizedAccessException or
					AssetNotFoundException or InvalidOperationException or ArgumentException)
			{
				Log.Warning($"Mesh warning: {exception.Message}");
			}
		}

		if (!string.IsNullOrWhiteSpace(_options.Collision))
		{
			try
			{
				LoadCollision(_options.Collision);
			}
			catch (Exception exception) when (
				exception is GenomeFormatException or Gar5FormatException or NotSupportedException or
					IOException or UnauthorizedAccessException or AssetNotFoundException or
					InvalidOperationException or ArgumentException)
			{
				Log.Warning($"Collision warning: {exception.Message}");
			}
		}

		if (_mesh is not null || _collisionLines is not null)
		{
			return;
		}

		if (string.IsNullOrWhiteSpace(_options.Texture))
		{
			return;
		}

		try
		{
			_texture = LoadTexture(_options.Texture);
		}
		catch (Exception exception) when (
			exception is DdsFormatException or NotSupportedException or IOException or
				UnauthorizedAccessException or AssetNotFoundException or InvalidOperationException)
		{
			Log.Warning($"Texture warning: {exception.Message}");
			_fallback = TextureLoader.CreateFallback(GraphicsDevice);
		}
	}

	protected override void Update(GameTime gameTime)
	{
		var started = Stopwatch.GetTimestamp();
		try
		{
			UpdateCore(gameTime);
		}
		finally
		{
			_lastUpdateMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
		}
	}

	private void UpdateCore(GameTime gameTime)
	{
		++_frameCount;
		++_updateCount;

		if (_deviceLost)
		{
			Log.Error("Graphics device lost; shutting down.");
			Exit();
			return;
		}

		var keyboard = Keyboard.GetState();
		if (keyboard.IsKeyDown(Keys.Escape) && _previousKeyboard.IsKeyUp(Keys.Escape))
		{
			Exit();
		}

		if (keyboard.IsKeyDown(Keys.F1) && _previousKeyboard.IsKeyUp(Keys.F1))
		{
			var visible = _hud?.ToggleDebugOverlay();
			if (visible is not null)
			{
				Log.Info($"Debug overlay {(visible.Value ? "shown" : "hidden")}.");
			}
		}

		_previousKeyboard = keyboard;

		if (_options.CrashTest && _updateCount == 3)
		{
			throw new InvalidOperationException("Crash screen test requested by --crash-test.");
		}

		if (_options.SoakSeconds is { } soakSeconds)
		{
			_soakElapsed += gameTime.ElapsedGameTime;
			if (_soakElapsed >= _soakNextSample)
			{
				_soakNextSample += SoakSampleInterval;
				LogMemorySample("Soak");
			}

			if (_soakElapsed >= TimeSpan.FromSeconds(soakSeconds))
			{
				LogMemorySample("Soak complete");
				Exit();
				return;
			}
		}
		else if (_options.SmokeTest && _updateCount >= SmokeTestFrameCount)
		{
			if (_renderer is not null)
			{
				Log.Info(
					$"Render statistics: drawCalls={_renderer.DrawCalls} " +
					$"triangles={_renderer.Triangles} meshes={_renderer.Meshes}");
			}

			Exit();
			return;
		}

		base.Update(gameTime);
	}

	protected override void Draw(GameTime gameTime)
	{
		var started = Stopwatch.GetTimestamp();
		try
		{
			DrawCore(gameTime);
		}
		finally
		{
			_lastDrawMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
			_frameStatistics.AddFrame(
				gameTime.ElapsedGameTime,
				_lastUpdateMilliseconds,
				_lastDrawMilliseconds);
		}
	}

	private void DrawCore(GameTime gameTime)
	{
		if (_renderer is null)
		{
			return;
		}

		_statisticsElapsed += gameTime.ElapsedGameTime.TotalSeconds;
		if (!_statisticsWarmedUp || _statisticsElapsed >= StatisticsInterval)
		{
			_statistics = _frameStatistics.Snapshot();
			if (_statisticsElapsed >= StatisticsInterval)
			{
				_statisticsElapsed = 0;
				_statisticsWarmedUp = true;
			}
		}

		if (_skeleton is not null)
		{
			DrawSkeleton(gameTime);
		}
		else if (_mesh is not null || _collisionLines is not null)
		{
			DrawMesh(gameTime);
		}
		else
		{
			DrawTexture();
		}

		_hud?.Update(_renderer, _statistics);
		_desktop?.Render();
		CaptureIfRequested();
		base.Draw(gameTime);
	}

	protected override void UnloadContent()
	{
		if (GraphicsDevice is not null)
		{
			GraphicsDevice.DeviceLost -= OnDeviceLost;
		}

		_renderer?.Dispose();
		_renderer = null;
		if (!ReferenceEquals(_texture, _fallback))
		{
			_texture?.Dispose();
		}

		_texture = null;
		_fallback?.Dispose();
		_fallback = null;
		_mesh?.Dispose();
		_mesh = null;
		_materials?.Dispose();
		_materials = null;
		_skeleton = null;
		_network = null;
		_clipA = null;
		_clipB = null;
		_desktop = null;
		_hud = null;
		base.UnloadContent();
	}

	private void CaptureIfRequested()
	{
		var captureFrame = _options.CaptureFrame ?? 4;
		if (_captured || string.IsNullOrEmpty(_options.CapturePath) || _frameCount < captureFrame)
		{
			return;
		}

		_captured = true;
		try
		{
			FrameCapture.Save(GraphicsDevice, _options.CapturePath);
		}
		catch (Exception exception) when (
			exception is IOException or UnauthorizedAccessException or NotSupportedException or
				ArgumentException)
		{
			Log.Error($"Capture failed for '{_options.CapturePath}'.", exception);
		}
	}

	private void ResizeBackBuffer()
	{
		if (_resizing)
		{
			return;
		}

		var bounds = Window.ClientBounds;
		if (bounds.Width <= 0 || bounds.Height <= 0)
		{
			return;
		}

		if (bounds.Width == _graphics.PreferredBackBufferWidth &&
			bounds.Height == _graphics.PreferredBackBufferHeight)
		{
			return;
		}

		_resizing = true;
		try
		{
			_graphics.PreferredBackBufferWidth = bounds.Width;
			_graphics.PreferredBackBufferHeight = bounds.Height;
			_graphics.ApplyChanges();
		}
		catch (Exception exception) when (
			exception is InvalidOperationException or ArgumentException or ObjectDisposedException)
		{
			Log.Warning($"Resize to {bounds.Width}x{bounds.Height} failed: {exception.Message}");
		}
		finally
		{
			_resizing = false;
		}
	}

	private void OnDeviceLost(object? sender, EventArgs eventArgs)
	{
		_deviceLost = true;
	}

	private void LogMemorySample(string prefix)
	{
		var memory = MemorySnapshot.CaptureCollected();
		Log.Info(
			$"{prefix} t={_soakElapsed.TotalSeconds:0}s " +
			$"workingSet={FormatMegabytes(memory.WorkingSetBytes)} " +
			$"managed={FormatMegabytes(memory.ManagedBytes)} fps={_statistics.FramesPerSecond}");
	}

	private static string FormatMegabytes(long bytes) => $"{bytes / 1048576.0:0.0} MB";

	private void LoadAnimationScene()
	{
		var resources = BuildResourceIndex();
		var skeletonQuery = string.IsNullOrWhiteSpace(_options.Mesh)
			? throw new ArgumentException("--animation requires --mesh <skn resource>.")
			: _options.Mesh;
		var skeletonResource = FindResource(resources, skeletonQuery, "skn");
		var skeletonData = ReadAll(skeletonResource.VirtualPath);
		var gmshOffset = skeletonData.AsSpan().IndexOf("GMSH"u8);
		if (gmshOffset < 0 || gmshOffset + 12 > skeletonData.Length)
		{
			throw new InvalidOperationException($"Skeleton '{skeletonResource.Name}' has no GMSH block.");
		}

		var tailOffset = gmshOffset + (int)BitConverter.ToUInt32(skeletonData, gmshOffset + 8);
		_skeleton = Skeleton.TryParse(skeletonData, tailOffset)
			?? throw new InvalidOperationException($"Skeleton '{skeletonResource.Name}' has an unrecognized tail.");
		if (_skeleton.Bones.Count == 0)
		{
			throw new InvalidOperationException($"Skeleton '{skeletonResource.Name}' has no bind-pose transforms.");
		}

		var networkResource = FindResource(resources, _options.Animation!, "net");
		_network = MotionNetwork.Parse(networkResource.Name, ReadAll(networkResource.VirtualPath));
		var clips = _network.Clips
			.Where(clip => clip.HasPose)
			.OrderByDescending(clip => clip.TrackNames.Count)
			.ThenBy(clip => clip.Index)
			.Take(2)
			.ToList();
		if (clips.Count == 0)
		{
			throw new InvalidOperationException($"Motion network '{networkResource.Name}' has no posed clips.");
		}

		_clipA = clips[0];
		_clipB = clips.Count > 1 ? clips[1] : clips[0];
		_sampleClip = _network.Clips
			.OrderByDescending(clip => clip.TrackNames.Count)
			.FirstOrDefault();
		_sampleGroup = _network.SampleGroups
			.Where(group => group.Tracks.Count >= 10)
			.OrderByDescending(group => group.Tracks.Count)
			.FirstOrDefault();
		Log.Info(
			$"Animation: skeleton={skeletonResource.Name} bones={_skeleton.Bones.Count} " +
			$"network={networkResource.Name} clips={_network.Clips.Count} " +
			$"blend={_clipA.Index}/{_clipB.Index} " +
			$"sampleTracks={_network.SampleTracks.Count} " +
			$"sampleGroup={_sampleGroup?.Duration:0.###}/{_sampleGroup?.Tracks.Count}");
	}

	private void DrawSkeleton(GameTime gameTime)
	{
		var skeleton = _skeleton!;
		var time = (float)gameTime.TotalGameTime.TotalSeconds;
		MotionPose pose;
		if (_sampleGroup is not null && _sampleClip is not null)
		{
			var trackPose = _sampleGroup.SamplePose(
				time % _sampleGroup.Duration,
				_sampleClip.TrackNames);
			var rotations = new System.Numerics.Quaternion[skeleton.Names.Count];
			for (var index = 0; index < rotations.Length; index++)
			{
				rotations[index] = System.Numerics.Quaternion.Identity;
			}

			for (var index = 0; index < skeleton.Names.Count; index++)
			{
				var track = _sampleClip.FindTrackIndex(skeleton.Names[index]);
				if (track >= 0 && track < trackPose.Count)
				{
					rotations[index] = trackPose.Rotations[track];
				}
			}

			pose = new MotionPose(rotations);
		}
		else
		{
			var transition = new MotionTransitionSampler(_clipA!, _clipB!, 2f);
			pose = transition.SampleLoop(time, skeleton.Names);
		}

		var skeletonPose = SkeletonPose.FromMotionPose(pose, skeleton);
		var matrices = skeletonPose.ComputeWorldMatrices();
		var points = new Vector3[matrices.Length];
		var min = new Vector3(float.MaxValue);
		var max = new Vector3(float.MinValue);
		for (var index = 0; index < matrices.Length; index++)
		{
			var translation = matrices[index].Translation;
			points[index] = new Vector3(translation.X, translation.Y, translation.Z);
			min = Vector3.Min(min, points[index]);
			max = Vector3.Max(max, points[index]);
		}

		if (points.Length == 0)
		{
			min = -Vector3.One;
			max = Vector3.One;
		}

		var center = (min + max) * 0.5f;
		var radius = Math.Max(0.001f, Vector3.Distance(center, max));
		var camera = center + new Vector3(
			MathF.Cos(time * 0.35f) * radius * 2.2f,
			radius * 1.1f,
			MathF.Sin(time * 0.35f) * radius * 2.2f);
		var view = Matrix.CreateLookAt(camera, center, Vector3.Up);
		var projection = Matrix.CreatePerspectiveFieldOfView(
			MathHelper.PiOver4,
			GraphicsDevice.Viewport.AspectRatio,
			radius * 0.001f,
			radius * 20f);

		var lines = new List<VertexPositionColor>(points.Length * 2);
		for (var index = 0; index < points.Length; index++)
		{
			var parent = skeletonPose.Parents[index];
			if (parent < 0)
			{
				continue;
			}

			var color = Color.Lerp(
				Color.Goldenrod,
				Color.Cyan,
				index / (float)Math.Max(1, points.Length - 1));
			lines.Add(new VertexPositionColor(points[parent], color));
			lines.Add(new VertexPositionColor(points[index], color));
		}

		_renderer!.BeginFrame(view, projection);
		_renderer.Clear(new Color(16, 18, 24));
		_renderer.DrawLines([.. lines], Matrix.Identity);
		_renderer.EndFrame();
	}

	private void DrawMesh(GameTime gameTime)
	{
		var box = _mesh is not null ?
			ComputeMeshBounds(_mesh) :
			new BoundingBox(
				new Vector3(_collision!.BoundsMin.X, _collision.BoundsMin.Y, _collision.BoundsMin.Z),
				new Vector3(_collision.BoundsMax.X, _collision.BoundsMax.Y, _collision.BoundsMax.Z));
		var center = (box.Min + box.Max) * 0.5f;
		var radius = Math.Max(0.001f, Vector3.Distance(center, box.Max));
		var time = (float)gameTime.TotalGameTime.TotalSeconds;
		var camera = center + new Vector3(
			MathF.Cos(time * 0.35f) * radius * 2.2f,
			radius * 1.1f,
			MathF.Sin(time * 0.35f) * radius * 2.2f);
		var view = Matrix.CreateLookAt(camera, center, Vector3.Up);
		var projection = Matrix.CreatePerspectiveFieldOfView(
			MathHelper.PiOver4,
			GraphicsDevice.Viewport.AspectRatio,
			radius * 0.001f,
			radius * 20f);

		_renderer!.BeginFrame(view, projection);
		_renderer.Clear(new Color(16, 18, 24));
		if (_mesh is not null)
		{
			_renderer.DrawMesh(_mesh, Matrix.Identity);
		}

		if (_collisionLines is not null)
		{
			_renderer.DrawLines(_collisionLines, Matrix.Identity, depthTest: false);
		}

		_renderer.EndFrame();
	}

	private void DrawTexture()
	{
		var texture = _texture ?? _fallback;
		_renderer!.BeginFrame();
		_renderer.Clear(new Color(16, 18, 24));
		if (texture is not null)
		{
			var viewport = GraphicsDevice.Viewport;
			var availableWidth = Math.Max(1, viewport.Width - TextureMargin * 2);
			var availableHeight = Math.Max(1, viewport.Height - TextureMargin * 2);
			var scale = Math.Min(
				availableWidth / (float)texture.Width,
				availableHeight / (float)texture.Height);
			var width = Math.Max(1, (int)MathF.Round(texture.Width * scale));
			var height = Math.Max(1, (int)MathF.Round(texture.Height * scale));
			var destination = new Rectangle(
				(viewport.Width - width) / 2,
				(viewport.Height - height) / 2,
				width,
				height);
			_renderer.DrawTexture(texture, destination);
		}

		_renderer.EndFrame();
	}

	private DrMesh LoadMesh(string query)
	{
		var resources = BuildResourceIndex();
		var resource = FindResource(resources, query, "msh");
		var mesh = GmshMesh.Parse(resource.Name, resource.VirtualPath, ReadAll(resource.VirtualPath));
		MeshMetadata? metadata = null;
		using (var classBlocks = new ResourceClassBlockReader(Assets))
		{
			try
			{
				metadata = MeshMetadata.Parse(classBlocks.Read(resource));
			}
			catch (Exception exception) when (exception is Gar5FormatException or GenomeFormatException)
			{
				Log.Warning($"Mesh metadata warning: {exception.Message}");
			}
		}

		_materials = new MaterialLibrary(GraphicsDevice, Assets, resources);
		var result = MeshLoader.Load(GraphicsDevice, mesh, metadata, _materials.Load);
		Log.Info(
			$"Mesh: {resource.Name} vertices={mesh.Positions.Count} triangles={mesh.TriangleCount} " +
			$"attributes={mesh.VertexAttributes.Count} subMeshes={result.MeshParts.Count} " +
			$"materials={metadata?.MaterialNames.Count ?? 0} lodStart={metadata?.LODRangeStart ?? 0:0.###} " +
			$"diagnostics={mesh.Diagnostics.Count}");
		foreach (var warning in _materials.Warnings)
		{
			Log.Warning($"Material warning: {warning}");
		}

		return result;
	}

	private void LoadCollision(string query)
	{
		var resources = BuildResourceIndex();
		var resource = FindResource(resources, query, "col");
		_collision = ElexCollisionMesh.Parse(
			resource.Name,
			resource.VirtualPath,
			ReadAll(resource.VirtualPath));
		var vertices = _collision.Vertices;
		var indices = _collision.Indices;
		var lines = new VertexPositionColor[indices.Count * 2];
		var cursor = 0;
		for (var triangle = 0; triangle < indices.Count; triangle += 3)
		{
			for (var edge = 0; edge < 3; edge++)
			{
				var from = indices[triangle + edge];
				var to = indices[triangle + (edge + 1) % 3];
				lines[cursor++] = new VertexPositionColor(ToXna(vertices[from]) * 100f, Color.Lime);
				lines[cursor++] = new VertexPositionColor(ToXna(vertices[to]) * 100f, Color.Lime);
			}
		}

		_collisionLines = lines;
		Log.Info(
			$"Collision: {resource.Name} vertices={vertices.Count} triangles={_collision.TriangleCount} " +
			$"lines={_collision.TriangleCount * 3} scale=100 (metres to mesh units)");
	}

	private static Vector3 ToXna(System.Numerics.Vector3 value) =>
		new(value.X, value.Y, value.Z);

	private static BoundingBox ComputeMeshBounds(DrMesh mesh)
	{
		BoundingBox? result = null;
		foreach (var part in mesh.MeshParts)
		{
			result = result is null
				? part.BoundingBox
				: BoundingBox.CreateMerged(result.Value, part.BoundingBox);
		}

		return result ?? new BoundingBox(-Vector3.One, Vector3.One);
	}

	private ElexResourceIndex BuildResourceIndex() =>
		ElexResourceIndex.Build(AssetCatalog.Build(Assets), Assets);

	private static ElexResource FindResource(ElexResourceIndex resources, string query, string typeToken)
	{
		return resources.Resources
			.Where(candidate => string.Equals(candidate.TypeToken, typeToken, StringComparison.OrdinalIgnoreCase))
			.OrderByDescending(candidate => string.Equals(candidate.Name, query, StringComparison.OrdinalIgnoreCase))
			.ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
			.FirstOrDefault(candidate =>
				string.Equals(candidate.Name, query, StringComparison.OrdinalIgnoreCase) ||
				candidate.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
			?? throw new AssetNotFoundException($"No {typeToken} resource matches '{query}'.");
	}

	private Texture2D LoadTexture(string query)
	{
		var resources = BuildResourceIndex();
		var resource = FindResource(resources, query, "img");
		var image = DdsImage.Parse(ReadAll(resource.VirtualPath));
		var normalMap = resource.Name.Contains("_Nm", StringComparison.OrdinalIgnoreCase);
		var texture = TextureLoader.Load(
			GraphicsDevice,
			image,
			new DdsDecodeOptions(NormalMap: normalMap));
		Log.Info(
			$"Texture: {resource.Name} {image.Width}x{image.Height} {image.FormatName} " +
			$"mips={image.MipCount} normalMap={normalMap}");
		return texture;
	}

	private byte[] ReadAll(string virtualPath)
	{
		using var stream = Assets.OpenRead(virtualPath);
		using var buffer = new MemoryStream();
		stream.CopyTo(buffer);
		return buffer.ToArray();
	}
}
