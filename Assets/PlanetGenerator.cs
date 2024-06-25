using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;
using Debug = UnityEngine.Debug;

public class PlanetGenerator : MonoBehaviour
{
    private struct Triangle
    {
        public Vector2 VertexA;
        public Vector2 VertexB;
        public Vector2 VertexC;
    }
    
    private struct Edge
    {
        public Vector2 VertexA;
        public Vector2 VertexB;
    }
    
    [FormerlySerializedAs("planetGenCs")] public ComputeShader chunkGeneratorCs;
    public ComputeShader pointMapGeneratorCs;
    public int resolution;
    [Range(0f, 1f)]
    public float isoValue;
    public int chunksPerAxis;

    [Header("Noise")]
    public float noiseScale;
    public Vector2 noiseOffset;
    public int noiseOctaves;
    [Range(0f, 1f)]
    public float noiseLacunarity;
    [Range(0, 10)]
    public int noisePower;
    [Range(-1f, 1f)]
    public float noiseValueCurveOffset;
    [Range(0f, 1f)]
    public float noiseRadialFalloffValue;
    
    private readonly List<int> _triangleInts = new();
    private MeshFilter _meshFilter;
    
    private ComputeBuffer _triangleBuffer;
    private ComputeBuffer _triangleCountBuffer;
    private ComputeBuffer _boundaryEdgeBuffer;
    private ComputeBuffer _boundaryEdgeCountBuffer;

    public RenderTexture _planetPointMap;

    private void Start()
    {
        InitializeData();
        GeneratePointMap();
        GeneratePlanet();
        
        var pointMapVisualizer = new GameObject("Point Map Visualizer");
        var rawImage = pointMapVisualizer.AddComponent<UnityEngine.UI.RawImage>();
        rawImage.texture = _planetPointMap;
        rawImage.rectTransform.sizeDelta = new Vector2(256, 256);
    }

    private void Update()
    {
        // GeneratePlanet();
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        // Debug.Log("rendering...");
        GeneratePointMap();
        Graphics.Blit(_planetPointMap, destination);
    }

    private void InitializeData()
    {
        _planetPointMap = new RenderTexture(resolution, resolution, 0);
        _planetPointMap.enableRandomWrite = true;
        _planetPointMap.Create();
        
        pointMapGeneratorCs.SetTexture(0, "point_map", _planetPointMap);
        
        _triangleBuffer = new ComputeBuffer((resolution / chunksPerAxis - 1) * (resolution / chunksPerAxis - 1) * 4, sizeof(float) * 6, ComputeBufferType.Append);
        _boundaryEdgeBuffer = new ComputeBuffer((resolution / chunksPerAxis - 1) * (resolution / chunksPerAxis - 1) * 4, sizeof(float) * 4, ComputeBufferType.Append);
        _triangleCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);
        _boundaryEdgeCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);
    }

    private void GeneratePointMap()
    {
        pointMapGeneratorCs.SetInt("res", resolution);
        pointMapGeneratorCs.SetFloat("noise_scale", noiseScale);
        pointMapGeneratorCs.SetFloat("noise_offset_x", noiseOffset.x);
        pointMapGeneratorCs.SetFloat("noise_offset_y", noiseOffset.y);
        pointMapGeneratorCs.SetInt("noise_octaves", noiseOctaves);
        pointMapGeneratorCs.SetFloat("noise_lacunarity", noiseLacunarity);
        pointMapGeneratorCs.SetInt("noise_power", noisePower);
        pointMapGeneratorCs.SetFloat("noise_value_curve_offset", noiseValueCurveOffset);
        pointMapGeneratorCs.SetFloat("noise_radial_falloff_value", noiseRadialFalloffValue);
        pointMapGeneratorCs.Dispatch(0, resolution / 8, resolution / 8, 1);
    }

    private void GeneratePlanet()
    {
        chunkGeneratorCs.SetInt("res", resolution);
        chunkGeneratorCs.SetInt("chunks_per_side", chunksPerAxis);
        chunkGeneratorCs.SetFloat("iso_value", isoValue);
        chunkGeneratorCs.SetBuffer(0, "triangles", _triangleBuffer);
        chunkGeneratorCs.SetBuffer(0, "boundary_edges", _boundaryEdgeBuffer);
        chunkGeneratorCs.SetTexture(0, "point_map", _planetPointMap);
        
        GenerateAllChunks();
    }

    private void GenerateAllChunks()
    {
        for (var y = 0; y < chunksPerAxis; y++)
        {
            for (var x = 0; x < chunksPerAxis; x++)
            {
                Debug.Log($"Generating chunk {x}, {y}");
                GenerateChunk(x, y);
            }
        }
        
        _triangleBuffer.Release();
        _triangleCountBuffer.Release();
        _boundaryEdgeBuffer.Release();
        _boundaryEdgeCountBuffer.Release();
    }

    private void GenerateChunk(int x, int y)
    {
        chunkGeneratorCs.SetInt("chunk_x", x);
        chunkGeneratorCs.SetInt("chunk_y", y);
        
        _triangleBuffer.SetCounterValue(1);
        _boundaryEdgeBuffer.SetCounterValue(1);
        
        // planetGenCs.Dispatch(0, resolution / 8, resolution / 8, 1);
        chunkGeneratorCs.Dispatch(0, 4, 4, 1);
        
        var outerTriangleCount = new int[1];
        var boundaryEdgeCount = new int[1];
        
        _triangleCountBuffer.SetData(outerTriangleCount);
        _boundaryEdgeCountBuffer.SetData(boundaryEdgeCount);
        
        ComputeBuffer.CopyCount(_triangleBuffer, _triangleCountBuffer, 0);
        ComputeBuffer.CopyCount(_boundaryEdgeBuffer, _boundaryEdgeCountBuffer, 0);
        
        _triangleCountBuffer.GetData(outerTriangleCount, 0, 0, 1);
        _boundaryEdgeCountBuffer.GetData(boundaryEdgeCount, 0, 0, 1);
        
        var triangles = new Triangle[outerTriangleCount[0]];
        var boundaryEdges = new Edge[boundaryEdgeCount[0]];
        
        _triangleBuffer.GetData(triangles, 0, 0, outerTriangleCount[0]);
        _boundaryEdgeBuffer.GetData(boundaryEdges, 0, 0, boundaryEdgeCount[0]);

        _triangleInts.Clear();

        // Because each cell is generated in parallel, every vertex has at least one duplicate.
        // We use a dictionary to keep track of the unique vertices and their indices to ignore duplicates.
        Dictionary<Vector3, int> vertexIndices = new();

        foreach (var tri in triangles)
        {
            if (!vertexIndices.ContainsKey(tri.VertexA))
            {
                vertexIndices[tri.VertexA] = vertexIndices.Count;
            }
            
            if (!vertexIndices.ContainsKey(tri.VertexB))
            {
                vertexIndices[tri.VertexB] = vertexIndices.Count;
            }
            
            if (!vertexIndices.ContainsKey(tri.VertexC))
            {
                vertexIndices[tri.VertexC] = vertexIndices.Count;
            }
            
            _triangleInts.Add(vertexIndices[tri.VertexA]);
            _triangleInts.Add(vertexIndices[tri.VertexB]);
            _triangleInts.Add(vertexIndices[tri.VertexC]);
        }

        var go = new GameObject($"Chunk ({x}, {y})");

        Dictionary<Edge, int> processedEdgeIndices = new();
        List<List<Vector2>> edgePaths = new();

        for (var ei = 0; ei < boundaryEdges.Length; ei++)
        {
            var edge = boundaryEdges[ei];
            
            if (processedEdgeIndices.ContainsKey(edge)) continue;
        
            var edgeLoop = new List<Vector2> { edge.VertexA, edge.VertexB };
            ProcessEdgePath(edge, edge, edgeLoop);
        
            if (edgeLoop.Count <= 2) continue;
        
            edgePaths.Add(edgeLoop);
        }
        
        for (var ei = 0; ei < edgePaths.Count; ei++)
        {
            var edgeVertices = edgePaths[ei];
            // var color = Color.HSVToRGB(ei / (float)edgeLoops.Count, 1, 1);
            
            // for (var i = 0; i < edgeVertices.Count - 1; i++)
            // {
            //     var color = Color.HSVToRGB(ei / (float)edgePaths.Count, i / (float)edgeVertices.Count / 1.2f, 1);
            //     var a = edgeVertices[i];
            //     var b = edgeVertices[(i + 1) % edgeVertices.Count];
            //     Debug.DrawLine(a, b, color, 300);
            // }

            var edgeCollider = go.AddComponent<EdgeCollider2D>();
            edgeCollider.points = edgeVertices.ToArray();
        }
        
        Debug.Log($"Edge loops: {edgePaths.Count}");

        var mesh = new Mesh();
        // mesh.indexFormat = IndexFormat.UInt32;
        
        go.AddComponent<MeshFilter>().mesh = mesh;
        go.AddComponent<MeshRenderer>().material = new Material(Shader.Find("Unlit/Texture"));
        
        mesh.vertices = vertexIndices.Keys.ToArray();
        mesh.triangles = _triangleInts.ToArray();
        mesh.RecalculateBounds();
        
        return;

        void ProcessEdgePath(Edge edge, Edge startEdge, List<Vector2> edgePath, bool backwards = false, int depth = 1)
        {
            if (depth == 1)
            {
                processedEdgeIndices[startEdge] = 0;
            }

            // stop if we've looped back to the start
            if (backwards && edge.VertexA == startEdge.VertexB)
            {
                if (edge.VertexB == startEdge.VertexB) return;
                edgePath.Insert(0, edge.VertexA);
                return;
            }

            if (!backwards && edge.VertexB == startEdge.VertexA)
            {
                if (edge.VertexA == startEdge.VertexA) return;
                edgePath.Add(edge.VertexB);
                return;
            }
            
            var nextEdgeFound = false;

            // recursively find the next edge in the path
            foreach (var otherEdge in boundaryEdges)
            {
                if (processedEdgeIndices.ContainsKey(otherEdge)) continue;
                
                if (edge.VertexA == otherEdge.VertexA) continue;

                // check if the edge is to the relative LEFT of the current edge
                if (depth == 1 || backwards)
                {
                    if (edge.VertexA == otherEdge.VertexB)
                    {
                        processedEdgeIndices[otherEdge] = 0;

                        // skip inserting vertices of the first edge because they're already in the list
                        if (depth != 1)
                        {
                            edgePath.Insert(0, edge.VertexA);
                        }
                        
                        ProcessEdgePath(otherEdge, startEdge, edgePath, true, depth + 1);
                        nextEdgeFound = true;
                        
                        if (depth == 1) continue;
                        return;
                    }
                }
                
                // check if the edge is to the relative RIGHT of the current edge
                if (edge.VertexB == otherEdge.VertexA)
                {
                    processedEdgeIndices[otherEdge] = 0;
                    
                    // skip inserting vertices of the first edge because they're already in the list
                    if (depth != 1)
                    {
                        edgePath.Add(edge.VertexB);
                    }
                    
                    ProcessEdgePath(otherEdge, startEdge, edgePath, false, depth + 1);
                    nextEdgeFound = true;
                }
            }
            
            if (nextEdgeFound) return;
            
            // final edge vertex in a non-looping path
            if (backwards)
            {
                edgePath.Insert(0, edge.VertexA);
            }
            else
            {
                edgePath.Add(edge.VertexB);
            }
        }
    }

    // private void OnDrawGizmos()
    // {
    //     foreach (var vertex in _vertices)
    //     {
    //         Gizmos.DrawWireSphere(vertex, 0.2f);
    //     }
    //
    //     for (var i = 0; i < _triangleInts.Count; i += 3)
    //     {
    //         Debug.DrawLine(_vertices[_triangleInts[i]], _vertices[_triangleInts[i + 1]], Color.red);
    //         Debug.DrawLine(_vertices[_triangleInts[i + 1]], _vertices[_triangleInts[i + 2]], Color.red);
    //         Debug.DrawLine(_vertices[_triangleInts[i + 2]], _vertices[_triangleInts[i]], Color.red);
    //     }
    // }
}
