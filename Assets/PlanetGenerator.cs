using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;

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
    public int resolution;
    [Range(0f, 1f)]
    public float isoValue;
    public int chunksPerAxis;

    [Header("Noise")]
    public float noiseScale;
    public Vector2 noiseOffset;
    
    private readonly List<int> _triangleInts = new();
    private MeshFilter _meshFilter;
    
    private ComputeBuffer _triangleBuffer;
    private ComputeBuffer _triangleCountBuffer;
    private ComputeBuffer _boundaryEdgeBuffer;
    private ComputeBuffer _boundaryEdgeCountBuffer;

    private void Start()
    {
        _triangleBuffer = new ComputeBuffer((resolution / chunksPerAxis - 1) * (resolution / chunksPerAxis - 1) * 4, sizeof(float) * 6, ComputeBufferType.Append);
        _boundaryEdgeBuffer = new ComputeBuffer((resolution / chunksPerAxis - 1) * (resolution / chunksPerAxis - 1) * 4, sizeof(float) * 4, ComputeBufferType.Append);
        _triangleCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);
        _boundaryEdgeCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);
        
        chunkGeneratorCs.SetInt("res", resolution);
        chunkGeneratorCs.SetInt("chunks_per_side", chunksPerAxis);
        chunkGeneratorCs.SetBuffer(0, "triangles", _triangleBuffer);
        chunkGeneratorCs.SetBuffer(0, "boundary_edges", _boundaryEdgeBuffer);
        
        GeneratePlanet();
    }

    private void Update()
    {
        // GeneratePlanet();
    }

    private void GeneratePlanet()
    {
        chunkGeneratorCs.SetFloat("iso_value", isoValue);
        chunkGeneratorCs.SetFloat("noise_scale", noiseScale);
        chunkGeneratorCs.SetFloat("noise_offset_x", noiseOffset.x);
        chunkGeneratorCs.SetFloat("noise_offset_y", noiseOffset.y);

        for (var y = 0; y < chunksPerAxis; y++)
        {
            for (var x = 0; x < chunksPerAxis; x++)
            {
                // debug
                // if (x != 0 || y != 0) continue;
                
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
        
        var innerTriangleCount = new int[1];
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

        // Uncomment this to only draw the outer triangles for debugging/funsies.
        // innerTriangles = Array.Empty<Triangle>();
        
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

        Dictionary<Edge, int> processedEdgeIndices = new();
        List<List<Vector2>> edgePaths = new();

        for (var ei = 0; ei < boundaryEdges.Length; ei++)
        {
            var edge = boundaryEdges[ei];
            if (processedEdgeIndices.ContainsKey(edge)) continue;
        
            var edgeLoop = new List<Vector2> { edge.VertexA };
            ProcessEdgePath(edge, edge, edgeLoop);
        
            if (edgeLoop.Count < 2) continue;
        
            edgePaths.Add(edgeLoop);
        }
        
        Debug.Log($"Edge loops: {edgePaths.Count}");

        var go = new GameObject($"Chunk ({x}, {y})");

        for (var ei = 0; ei < edgePaths.Count; ei++)
        {
            var edgeLoop = edgePaths[ei];
            // var color = Color.HSVToRGB(ei / (float)edgeLoops.Count, 1, 1);
            
            // for (var i = 0; i < edgeLoop.Count - 1; i++)
            // {
            //     var color = Color.HSVToRGB(ei / (float)edgeLoops.Count, i / (float)edgeLoop.Count, 1);
            //     var a = edgeLoop[i];
            //     var b = edgeLoop[(i + 1) % edgeLoop.Count];
            //     Debug.DrawLine(a, b, color, 300);
            // }

            var edgeCollider = go.AddComponent<EdgeCollider2D>();
            edgeCollider.points = edgeLoop.ToArray();
        }

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
            
            var processingFurther = false;

            // recursively find the next edge in the path
            foreach (var otherEdge in boundaryEdges)
            {
                if (processedEdgeIndices.ContainsKey(otherEdge)) continue;
                
                if (edge.VertexA == otherEdge.VertexA) continue;

                if (depth == 1 || backwards)
                {
                    if (edge.VertexA == otherEdge.VertexB)
                    {
                        processedEdgeIndices[otherEdge] = 0;
                        edgePath.Insert(0, edge.VertexA);
                        ProcessEdgePath(otherEdge, startEdge, edgePath, true, depth + 1);
                        processingFurther = true;
                        continue;
                    }
                }
                
                if (edge.VertexB == otherEdge.VertexA)
                {
                    processedEdgeIndices[otherEdge] = 0;
                    edgePath.Add(edge.VertexB);
                    ProcessEdgePath(otherEdge, startEdge, edgePath, false, depth + 1);
                    processingFurther = true;
                }
            }
            
            if (processingFurther) return;
            
            // final edge vertex in a non-closed path
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
