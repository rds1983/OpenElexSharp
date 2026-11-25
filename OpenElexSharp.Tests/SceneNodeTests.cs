using Microsoft.Xna.Framework;
using OpenElexSharp.Graphics;

namespace OpenElexSharp.Tests;

public class SceneNodeTests
{
	[Fact]
	public void WorldTransform_ComposesLocalWithParent()
	{
		var root = new SceneNode("root")
		{
			LocalTransform = Matrix.CreateTranslation(10, 0, 0),
		};
		var child = new SceneNode("child")
		{
			LocalTransform = Matrix.CreateTranslation(0, 5, 0),
		};
		root.AddChild(child);

		var expected = Matrix.CreateTranslation(0, 5, 0) * Matrix.CreateTranslation(10, 0, 0);
		Assert.Equal(expected, child.WorldTransform);
		Assert.Equal(new Vector3(10, 5, 0), child.WorldTransform.Translation);
	}

	[Fact]
	public void WorldTransform_RecomposesAfterParentChange()
	{
		var first = new SceneNode("first")
		{
			LocalTransform = Matrix.CreateTranslation(10, 0, 0),
		};
		var second = new SceneNode("second")
		{
			LocalTransform = Matrix.CreateTranslation(20, 0, 0),
		};
		var child = new SceneNode("child")
		{
			LocalTransform = Matrix.CreateTranslation(1, 0, 0),
		};
		first.AddChild(child);
		Assert.Equal(new Vector3(11, 0, 0), child.WorldTransform.Translation);

		first.RemoveChild(child);
		second.AddChild(child);

		Assert.Equal(new Vector3(21, 0, 0), child.WorldTransform.Translation);
	}

	[Fact]
	public void WorldTransform_RecomposesAfterParentLocalChanges()
	{
		var root = new SceneNode("root");
		var child = new SceneNode("child")
		{
			LocalTransform = Matrix.CreateTranslation(0, 0, 2),
		};
		root.AddChild(child);
		Assert.Equal(new Vector3(0, 0, 2), child.WorldTransform.Translation);

		root.LocalTransform = Matrix.CreateTranslation(0, 3, 0);

		Assert.Equal(new Vector3(0, 3, 2), child.WorldTransform.Translation);
	}

	[Fact]
	public void UpdateWorldTransform_UpdatesDescendants()
	{
		var root = new SceneNode("root")
		{
			LocalTransform = Matrix.CreateRotationY(0.5f),
		};
		var child = new SceneNode("child")
		{
			LocalTransform = Matrix.CreateTranslation(0, 0, 5),
		};
		var grandchild = new SceneNode("grandchild")
		{
			LocalTransform = Matrix.CreateTranslation(1, 0, 0),
		};
		root.AddChild(child);
		child.AddChild(grandchild);
		root.UpdateWorldTransform();

		var expected = Matrix.CreateTranslation(0, 0, 5) * root.LocalTransform;
		Assert.Equal(expected, child.WorldTransform);
		Assert.Equal(
			Matrix.CreateTranslation(1, 0, 0) * expected,
			grandchild.WorldTransform);
	}

	[Fact]
	public void AddChild_RejectsCycles()
	{
		var root = new SceneNode("root");
		var child = new SceneNode("child");
		root.AddChild(child);

		Assert.Throws<ArgumentException>(() => child.AddChild(root));
		Assert.Throws<ArgumentException>(() => root.AddChild(root));
	}

	[Fact]
	public void AddChild_MovesNodeFromPreviousParent()
	{
		var first = new SceneNode("first");
		var second = new SceneNode("second");
		var child = new SceneNode("child");
		first.AddChild(child);
		second.AddChild(child);

		Assert.Empty(first.Children);
		Assert.Single(second.Children);
		Assert.Same(second, child.Parent);
	}

	[Fact]
	public void ForEach_VisitsDepthFirst()
	{
		var root = new SceneNode("root");
		var first = new SceneNode("first");
		var second = new SceneNode("second");
		root.AddChild(first);
		root.AddChild(second);
		var names = new List<string>();

		root.ForEach(node => names.Add(node.Name));

		Assert.Equal(["root", "first", "second"], names);
	}

	[Fact]
	public void NodeWithoutMesh_HasNoBounds()
	{
		var node = new SceneNode("empty");

		Assert.False(node.HasBounds);
		Assert.Equal(0, node.PartCount);
	}
}
