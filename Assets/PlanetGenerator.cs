using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class PlanetGenerator : MonoBehaviour
{
    private struct Triangle
    {
        public Vector2 VertexA;
        public Vector2 VertexB;
        public Vector2 VertexC;
    }
    
    public ComputeShader planetGenCs;
    public int resolution;
    [Range(0f, 1f)]
    public float isoValue;
    public int chunksPerAxis;

    [Header("Noise")]
    public float noiseScale;
    public Vector2 noiseOffset;
    
    private readonly List<Vector3> _vertices = new();
    private readonly List<int> _triangleInts = new();
    private MeshFilter _meshFilter;
    private ComputeBuffer _innerTriangleBuffer;
    private ComputeBuffer _outerTriangleBuffer;
    private ComputeBuffer _innerTriangleCountBuffer;
    private ComputeBuffer _outerTriangleCountBuffer;

    private void Start()
    {
        _innerTriangleBuffer = new ComputeBuffer((resolution / chunksPerAxis - 1) * (resolution / chunksPerAxis - 1) * 4, sizeof(float) * 6, ComputeBufferType.Append);
        _outerTriangleBuffer = new ComputeBuffer((resolution / chunksPerAxis - 1) * (resolution / chunksPerAxis - 1) * 4, sizeof(float) * 6, ComputeBufferType.Append);
        _innerTriangleCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);
        _outerTriangleCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);
        
        planetGenCs.SetInt("res", resolution);
        planetGenCs.SetInt("chunks_per_side", chunksPerAxis);
        planetGenCs.SetBuffer(0, "inner_triangles", _innerTriangleBuffer);
        planetGenCs.SetBuffer(0, "outer_triangles", _outerTriangleBuffer);
        
        GeneratePlanet();
    }

    private void Update()
    {
        // GeneratePlanet();
    }

    private void GeneratePlanet()
    {
        planetGenCs.SetFloat("iso_value", isoValue);
        planetGenCs.SetFloat("noise_scale", noiseScale);
        planetGenCs.SetFloat("noise_offset_x", noiseOffset.x);
        planetGenCs.SetFloat("noise_offset_y", noiseOffset.y);

        for (var y = 0; y < chunksPerAxis; y++)
        {
            for (var x = 0; x < chunksPerAxis; x++)
            {
                Debug.Log($"Generating chunk {x}, {y}");
                GenerateChunk(x, y);
            }
        }
        
        _innerTriangleBuffer.Release();
        _outerTriangleBuffer.Release();
        _innerTriangleCountBuffer.Release();
        _outerTriangleCountBuffer.Release();
        
        Debug.Log("Planet generated");
    }

    private void GenerateChunk(int x, int y)
    {
        planetGenCs.SetInt("chunk_x", x);
        planetGenCs.SetInt("chunk_y", y);
        
        _innerTriangleBuffer.SetCounterValue(1);
        _outerTriangleBuffer.SetCounterValue(1);
        
        // planetGenCs.Dispatch(0, resolution / 8, resolution / 8, 1);
        planetGenCs.Dispatch(0, 4, 4, 1);
        
        var innerTriangleCount = new int[1];
        var outerTriangleCount = new int[1];
        _innerTriangleCountBuffer.SetData(innerTriangleCount);
        _outerTriangleCountBuffer.SetData(outerTriangleCount);
        ComputeBuffer.CopyCount(_innerTriangleBuffer, _innerTriangleCountBuffer, 0);
        ComputeBuffer.CopyCount(_outerTriangleBuffer, _outerTriangleCountBuffer, 0);
        _innerTriangleCountBuffer.GetData(innerTriangleCount, 0, 0, 1);
        _outerTriangleCountBuffer.GetData(outerTriangleCount, 0, 0, 1);
        
        var innerTriangles = new Triangle[innerTriangleCount[0]];
        var outerTriangles = new Triangle[outerTriangleCount[0]];
        _innerTriangleBuffer.GetData(innerTriangles, 0, 0, innerTriangleCount[0]);
        _outerTriangleBuffer.GetData(outerTriangles, 0, 0, outerTriangleCount[0]);

        _vertices.Clear();
        _triangleInts.Clear();

        // Uncomment this to only draw the outer triangles for debugging/funsies.
        // innerTriangles = Array.Empty<Triangle>();
        
        // Because each cell is generated in parallel, every vertex has at least one duplicate.
        // We use a dictionary to keep track of the unique vertices and their indices to ignore duplicates.
        Dictionary<Vector3, int> vertexIndices = new();

        foreach (var tri in innerTriangles)
        {
            AddTriangle(tri);
        }
        
        foreach (var tri in outerTriangles)
        {
            AddTriangle(tri);
        }
        
        var mesh = new Mesh();
        // mesh.indexFormat = IndexFormat.UInt32;
        
        var go = new GameObject("Chunk");
        go.AddComponent<MeshFilter>().mesh = mesh;
        go.AddComponent<MeshRenderer>().material = new Material(Shader.Find("Unlit/Texture"));
        // var polyCollider = go.AddComponent<PolygonCollider2D>();
        
        mesh.vertices = vertexIndices.Keys.ToArray();
        mesh.triangles = _triangleInts.ToArray();
        mesh.RecalculateBounds();
        return;

        void AddTriangle(Triangle tri)
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
