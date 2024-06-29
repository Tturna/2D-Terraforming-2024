using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EdgeQuadtreeTest : MonoBehaviour
{
    void Start()
    {
        var qtBounds = new Vector4(0f, 0f, 256f, 256f);
        var qt = new EdgeQuadtree(qtBounds);
        
        // generate test edges
        for (var i = 0; i < 16; i += 2)
        {
            var edge = new PlanetGenerator.Edge
            {
                VertexA = new Vector2(i, i),
                VertexB = new Vector2(i + 1, i + 1)
            };
            
            qt.Insert(edge);
        }
        
        var nearbyEdges = qt.GetLeafIncludingPoint(Vector2.zero);

        if (nearbyEdges == null)
        {
            throw new NullReferenceException("nearbyEdges is null");
        }
        
        Debug.Log("query 1");
        foreach (var edge in nearbyEdges)
        {
            Debug.Log($"Edge: ({edge.VertexA}, {edge.VertexB})");
        }

        nearbyEdges = qt.GetLeafIncludingPoint(Vector2.one * 14);
        
        if (nearbyEdges == null)
        {
            throw new NullReferenceException("nearbyEdges is null");
        }
        
        Debug.Log("query 2");
        foreach (var edge in nearbyEdges)
        {
            Debug.Log($"Edge: ({edge.VertexA}, {edge.VertexB})");
        }
    }
}
