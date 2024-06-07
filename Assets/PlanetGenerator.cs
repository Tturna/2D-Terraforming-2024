using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class PlanetGenerator : MonoBehaviour
{
    private struct Triangle
    {
        public Vector2 VertexA;
        public Vector2 VertexB;
        public Vector2 VertexC;
    }
    
    public ComputeShader planetGenCs;
    public Texture2D sourceTexture;
    public int resolution;
    [Range(0f, 1f)]
    public float isoValue;
    public int chunksPerAxis;

    [Header("Noise")]
    public float noiseScale;
    public Vector2 noiseOffset;
    
    private readonly List<Vector3> _vertices = new();
    private readonly List<int> _triangleInts = new();
    private Mesh _mesh;
    private MeshFilter _meshFilter;
    private ComputeBuffer _triangleBuffer;
    private ComputeBuffer _triangleCountBuffer;

    private void Start()
    {
        _mesh = new Mesh();
        // TODO: Use UInt16 index format when you have chunks
        _mesh.indexFormat = IndexFormat.UInt32;
        
        var go = new GameObject("Planet");
        go.AddComponent<MeshFilter>().mesh = _mesh;
        go.AddComponent<MeshRenderer>().material = new Material(Shader.Find("Unlit/Texture"));
        
        _triangleBuffer = new ComputeBuffer((resolution - 1) * (resolution - 1) * 4, sizeof(float) * 6, ComputeBufferType.Append);
        _triangleCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);
        planetGenCs.SetFloat("resolution", resolution);
        planetGenCs.SetTexture(0, "source", sourceTexture);
        
        planetGenCs.SetBuffer(0, "triangles", _triangleBuffer);
        
        GeneratePlanet();
    }

    private void Update()
    {
        // GeneratePlanet();
    }

    private void GeneratePlanet()
    {
        _triangleBuffer.SetCounterValue(0);
        
        planetGenCs.SetFloat("iso_value", isoValue);
        planetGenCs.SetFloat("noise_scale", noiseScale);
        planetGenCs.SetFloat("noise_offset_x", noiseOffset.x);
        planetGenCs.SetFloat("noise_offset_y", noiseOffset.y);
        planetGenCs.Dispatch(0, resolution / 8, resolution / 8, 1);
        
        var triangleCount = new int[1];
        _triangleCountBuffer.SetData(triangleCount);
        ComputeBuffer.CopyCount(_triangleBuffer, _triangleCountBuffer, 0);
        _triangleCountBuffer.GetData(triangleCount, 0, 0, 1);
        
        var triangles = new Triangle[triangleCount[0]];
        _triangleBuffer.GetData(triangles, 0, 0, triangleCount[0]);

        // This system generates duplicate vertices
        _vertices.Clear();
        _triangleInts.Clear();

        for (var i = 0; i < triangles.Length; i++)
        {
            var tri = triangles[i];
            _vertices.Add(tri.VertexA);
            _vertices.Add(tri.VertexB);
            _vertices.Add(tri.VertexC);
            _triangleInts.Add(i * 3);
            _triangleInts.Add(i * 3 + 1);
            _triangleInts.Add(i * 3 + 2);
        }
        
        _mesh.vertices = _vertices.ToArray();
        _mesh.triangles = _triangleInts.ToArray();
        _mesh.RecalculateBounds();
        
        // _triangleBuffer.Release();
        // _triangleCountBuffer.Release();
        
        // Debug.Log("Planet generated");
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
