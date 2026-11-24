using NUnit.Framework;
using UnityEngine;

namespace Cartography.Tests
{
    public sealed class MapTests
    {
        static Map TwoRooms(out int a, out int b, out int below)
        {
            var builder = new MapBuilder();
            a = builder.AddRegion(0, new Vector3(0f, 0f, 0f), new Vector2(12f, 12f), 0f, 0f, 4f);
            b = builder.AddRegion(0, new Vector3(12f, 0f, 0f), new Vector2(12f, 12f), 0f, 0f, 4f);
            below = builder.AddRegion(1, new Vector3(0f, -6f, 0f), new Vector2(12f, 12f), 90f, -6f, -2f);
            builder.Connect(a, below);
            builder.AddWall(0, new Vector3(6f, 0f, -6f), new Vector3(6f, 0f, 6f));
            return builder.Build();
        }

        [Test]
        public void FindsRegionAndLayerByHeight()
        {
            using var map = TwoRooms(out int a, out int b, out int below);
            Assert.AreEqual(2, map.Layers.Count);
            Assert.AreEqual(a, map.RegionAt(new Vector3(1f, 1f, 1f)));
            Assert.AreEqual(b, map.RegionAt(new Vector3(13f, 1f, 1f)));
            Assert.AreEqual(below, map.RegionAt(new Vector3(1f, -5f, 1f)));
            Assert.AreEqual(1, map.LayerAt(new Vector3(1f, -5f, 1f), 0));
            Assert.AreEqual(7, map.LayerAt(new Vector3(100f, 0f, 0f), 7));
        }

        [Test]
        public void RevealStaysInRegionAndNeighbours()
        {
            using var map = TwoRooms(out _, out _, out _);
            map.RevealAround(new Vector3(5f, 1f, 0f), 10f);
            Assert.Greater(map.SeenAt(new Vector3(2f, 0f, 0f), 0), 0.9f);
            // b is next to a but not joined to it.
            Assert.AreEqual(0f, map.SeenAt(new Vector3(8f, 0f, 0f), 0));
        }

        [Test]
        public void WallsAndFloorAreLaidOut()
        {
            using var map = TwoRooms(out _, out _, out _);
            var layer = map.Layer(0);
            Assert.IsTrue(layer.IsFloor(new Vector3(0f, 0f, 0f)));
            Assert.IsFalse(layer.IsFloor(new Vector3(0f, 0f, 20f)));
            Assert.IsTrue(layer.TryCell(new Vector3(6f, 0f, 0f), out int x, out int y));
            Assert.Greater(layer.walls[y * layer.Width + x], 128);
        }

        [Test]
        public void RevealRoundTrips()
        {
            using var source = TwoRooms(out _, out int b, out _);
            source.RevealRegion(b);
            source.RevealAround(new Vector3(0f, -5f, 0f), 4f);
            var data = source.SaveReveal();

            using var target = TwoRooms(out _, out _, out _);
            Assert.IsTrue(target.LoadReveal(data));
            Assert.AreEqual(1f, target.SeenAt(new Vector3(12f, 0f, 0f), 0));
            Assert.Greater(target.SeenAt(new Vector3(0f, -6f, 0f), 1), 0.9f);
            Assert.AreEqual(0f, target.SeenAt(new Vector3(0f, 0f, 0f), 0));

            Assert.IsFalse(target.LoadReveal(new byte[] { 9 }));
        }
    }
}
