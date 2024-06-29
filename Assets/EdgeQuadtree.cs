using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;
using Edge = PlanetGenerator.Edge;

public class EdgeQuadtree
{
    private Vector4 Bounds { get; }
    private List<Edge> Edges { get; set; }
    private bool Subdivided { get; set; }
    
    private const int MaxBranchElements = 4;

    private EdgeQuadtree _topLeft;
    private EdgeQuadtree _topRight;
    private EdgeQuadtree _bottomLeft;
    private EdgeQuadtree _bottomRight;

    public EdgeQuadtree(Vector4 bounds)
    {
        Bounds = bounds;
        Edges = new List<Edge>();
    }

    private bool Contains(Edge edge)
    {
        // With marching squares, edges will always be basically equal size.
        // Because of this, it's ok to simply consider if an edge starts
        // within the bounds of a quad. This is opposed to for example,
        // calculating the average of the edge points to find the middle of the
        // edge and using that to find the correct quad.
        return Contains(edge.VertexA);
    }

    private bool Contains(Vector2 point)
    {
        var x = Bounds.x;
        var y = Bounds.y;
        var w = Bounds.z;
        var h = Bounds.w;
        var px = point.x;
        var py = point.y;
        return px >= x && px <= x + w && py >= y && py <= y + h;
    }

    private void Subdivide()
    {
        var tlBounds = new Vector4(Bounds.x, Bounds.y + Bounds.w / 2, Bounds.z / 2, Bounds.w / 2);
        var trBounds = new Vector4(Bounds.x + Bounds.z / 2, Bounds.y + Bounds.w / 2, Bounds.z / 2, Bounds.w / 2);
        var blBounds = new Vector4(Bounds.x, Bounds.y, Bounds.z / 2, Bounds.w / 2);
        var brBounds = new Vector4(Bounds.x + Bounds.z / 2, Bounds.y, Bounds.z / 2, Bounds.w / 2);
        
        _topLeft = new EdgeQuadtree(tlBounds);
        _topRight = new EdgeQuadtree(trBounds);
        _bottomLeft = new EdgeQuadtree(blBounds);
        _bottomRight = new EdgeQuadtree(brBounds);
        Subdivided = true;

        foreach (var edge in Edges)
        {
            Insert(edge);
        }

        Edges = null;
    }

    public void Insert(Edge edge)
    {
        if (!Contains(edge)) return;

        if (!Subdivided && Edges?.Count > MaxBranchElements)
        {
            Subdivide();
        }

        if (Subdivided)
        {
            _topLeft.Insert(edge);
            _topRight.Insert(edge);
            _bottomLeft.Insert(edge);
            _bottomRight.Insert(edge);
        }
        else
        {
            // edges is only null if this quad is subdivided
            Edges!.Add(edge);
        }
    }

    [CanBeNull]
    public List<Edge> GetLeafIncludingPoint(Vector2 point)
    {
        if (!Contains(point)) return null;

        if (!Subdivided) return Edges;
        
        var res = _topLeft.GetLeafIncludingPoint(point);
        if (res != null) return res;

        res = _topRight.GetLeafIncludingPoint(point);
        if (res != null) return res;

        res = _bottomLeft.GetLeafIncludingPoint(point);
        if (res != null) return res;

        res = _bottomRight.GetLeafIncludingPoint(point);
        return res;
    }
}
