using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

public class PlanetGenerator : MonoBehaviour
{
    private struct Triangle
    {
        public Vector2 VertexA;
        public Vector2 VertexB;
        public Vector2 VertexC;
    }
    
    public struct Edge
    {
        public Vector2 VertexA;
        public Vector2 VertexB;
    }

    private struct Chunk
    {
        public GameObject GameObject;
        public Mesh Mesh;
    }
    
    [FormerlySerializedAs("planetGenCs")] public ComputeShader chunkGeneratorCs;
    public ComputeShader pointMapGeneratorCs;
    public ComputeShader terraformerCs;
    public int resolution;
    [Range(0f, 1f)]
    public float isoValue;
    public int chunksPerAxis;
    public Material meshMaterial;
    [Range(0.02f, 1f)]
    public float brushStrength;

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
    [Range(1f, 10f)]
    public float noiseOutwardFadeStartMultiplier;
    
    private readonly List<int> _triangleInts = new();
    private MeshFilter _meshFilter;
    
    private ComputeBuffer _triangleBuffer;
    private ComputeBuffer _triangleCountBuffer;
    private ComputeBuffer _boundaryEdgeBuffer;
    private ComputeBuffer _boundaryEdgeCountBuffer;
    private bool _buffersInitialized;
    private float _bufferReleaseTimer;
    private const float BufferReleaseTimeout = 3f;

    // Because each cell is generated in parallel, every vertex has at least one duplicate.
    // We use a dictionary to keep track of the unique vertices and their indices to ignore duplicates.
    private readonly Dictionary<Vector3, int> _uniqueVertexIndices = new();
    private readonly Dictionary<Edge, int> _processedEdgeIndices = new();
    private readonly List<List<Vector2>> _edgePaths = new();
    private readonly int[] _triangleCount = new int[1];
    private readonly int[] _boundaryEdgeCount = new int[1];
        
    private Chunk[] _chunks;
    private RenderTexture _planetPointMap;

    private void Start()
    {
        var sw = new Stopwatch();
        sw.Start();
        InitializeData();
        GeneratePointMap();
        sw.Stop();
        Debug.Log($"Initialization took {sw.ElapsedMilliseconds}ms");
        GeneratePlanet();
        
        // var pointMapVisualizer = new GameObject("Point Map Visualizer");
        // var rawImage = pointMapVisualizer.AddComponent<UnityEngine.UI.RawImage>();
        // rawImage.texture = _planetPointMap;
        // rawImage.rectTransform.sizeDelta = new Vector2(256, 256);
    }

    private void Update()
    {
        // GeneratePlanet();

        if (_buffersInitialized)
        {
            _bufferReleaseTimer += Time.deltaTime;
            if (_bufferReleaseTimer >= BufferReleaseTimeout)
            {
                ReleaseBuffers();
                _bufferReleaseTimer = 0;
            }
        }

        if (Input.GetMouseButton(0))
        {
            var mouseWorldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            var localBrushPosition = mouseWorldPos - transform.position;
            
            if (localBrushPosition.x < 0 || localBrushPosition.x >= resolution ||
                localBrushPosition.y < 0 || localBrushPosition.y >= resolution)
            {
                return;
            }
            
            Terraform(localBrushPosition, 1f, -brushStrength);
        }
        else if (Input.GetMouseButton(1))
        {
            var mouseWorldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            var localBrushPosition = mouseWorldPos - transform.position;

            if (localBrushPosition.x >= 0 && localBrushPosition.x < resolution
                && localBrushPosition.y >= 0 && localBrushPosition.y < resolution)
            {
                Terraform(localBrushPosition, 1f, brushStrength);
            }
        }
    }

    // private void OnRenderImage(RenderTexture source, RenderTexture destination)
    // {
    //     // Debug.Log("rendering...");
    //     GeneratePointMap();
    //     Graphics.Blit(_planetPointMap, destination);
    // }

    private void InitializeData()
    {
        // ReSharper disable once UseObjectOrCollectionInitializer
        _planetPointMap = new RenderTexture(resolution, resolution, 0);
        _planetPointMap.enableRandomWrite = true;
        _planetPointMap.Create();
        
        pointMapGeneratorCs.SetInt("res", resolution);
        pointMapGeneratorCs.SetTexture(0, "point_map", _planetPointMap);
        terraformerCs.SetInt("res", resolution);
        terraformerCs.SetTexture(0, "point_map", _planetPointMap);
        
        InitializeBuffers();
        
        _chunks = new Chunk[chunksPerAxis * chunksPerAxis];

        for (var i = 0; i < _chunks.Length; i++)
        {
            var x = i % chunksPerAxis;
            var y = i / chunksPerAxis;
            var go = new GameObject($"Chunk ({x}, {y})");
            var mesh = new Mesh();
            // mesh.indexFormat = IndexFormat.UInt32;
            
            go.AddComponent<MeshFilter>().mesh = mesh;
            go.AddComponent<MeshRenderer>().material = meshMaterial;
            
            _chunks[i].GameObject = go;
            _chunks[i].Mesh = mesh;
        }
    }

    private void InitializeBuffers()
    {
        _triangleBuffer = new ComputeBuffer((resolution / chunksPerAxis - 1) * (resolution / chunksPerAxis - 1) * 4, sizeof(float) * 6, ComputeBufferType.Append);
        _boundaryEdgeBuffer = new ComputeBuffer((resolution / chunksPerAxis - 1) * (resolution / chunksPerAxis - 1) * 4, sizeof(float) * 4, ComputeBufferType.Append);
        _triangleCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);
        _boundaryEdgeCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);
        _buffersInitialized = true;
        _bufferReleaseTimer = 0;
        
        chunkGeneratorCs.SetBuffer(0, "triangles", _triangleBuffer);
        chunkGeneratorCs.SetBuffer(0, "boundary_edges", _boundaryEdgeBuffer);
    }

    private void ReleaseBuffers()
    {
        _triangleBuffer.Release();
        _boundaryEdgeBuffer.Release();
        _triangleCountBuffer.Release();
        _boundaryEdgeCountBuffer.Release();
        _buffersInitialized = false;
    }

    private void GeneratePointMap()
    {
        pointMapGeneratorCs.SetFloat("noise_scale", noiseScale);
        pointMapGeneratorCs.SetFloat("noise_offset_x", noiseOffset.x);
        pointMapGeneratorCs.SetFloat("noise_offset_y", noiseOffset.y);
        pointMapGeneratorCs.SetInt("noise_octaves", noiseOctaves);
        pointMapGeneratorCs.SetFloat("noise_lacunarity", noiseLacunarity);
        pointMapGeneratorCs.SetInt("noise_power", noisePower);
        pointMapGeneratorCs.SetFloat("noise_value_curve_offset", noiseValueCurveOffset);
        pointMapGeneratorCs.SetFloat("noise_radial_falloff_value", noiseRadialFalloffValue);
        pointMapGeneratorCs.SetFloat("noise_outward_fade_start_multiplier", noiseOutwardFadeStartMultiplier);
        pointMapGeneratorCs.Dispatch(0, resolution / 8, resolution / 8, 1);
    }

    public void GeneratePlanet()
    {
        chunkGeneratorCs.SetInt("res", resolution);
        chunkGeneratorCs.SetInt("chunks_per_side", chunksPerAxis);
        chunkGeneratorCs.SetFloat("iso_value", isoValue);
        chunkGeneratorCs.SetTexture(0, "point_map", _planetPointMap);

        var sw = new Stopwatch();
        sw.Start();
        GenerateAllChunks();
        sw.Stop();
        Debug.Log($"Total chunk generation took {sw.ElapsedMilliseconds}ms");
    }

    private void GenerateAllChunks()
    {
        for (var y = 0; y < chunksPerAxis; y++)
        {
            for (var x = 0; x < chunksPerAxis; x++)
            {
                // Debug.Log($"Generating chunk {x}, {y}");
                ProcessChunk(x, y);
            }
        }
        
        ReleaseBuffers();
    }

    private void ProcessChunk(int x, int y)
    {
        var chunk = _chunks[y * chunksPerAxis + x];
        
        chunkGeneratorCs.SetInt("chunk_x", x);
        chunkGeneratorCs.SetInt("chunk_y", y);
        
        _triangleBuffer.SetCounterValue(1);
        _boundaryEdgeBuffer.SetCounterValue(1);
        
        var threadGroups = resolution / chunksPerAxis / 8;
        chunkGeneratorCs.Dispatch(0, threadGroups, threadGroups, 1);
        
        _triangleCountBuffer.SetData(_triangleCount);
        _boundaryEdgeCountBuffer.SetData(_boundaryEdgeCount);
        
        ComputeBuffer.CopyCount(_triangleBuffer, _triangleCountBuffer, 0);
        ComputeBuffer.CopyCount(_boundaryEdgeBuffer, _boundaryEdgeCountBuffer, 0);
        
        _triangleCountBuffer.GetData(_triangleCount, 0, 0, 1);
        _boundaryEdgeCountBuffer.GetData(_boundaryEdgeCount, 0, 0, 1);
        
        var triangles = new Triangle[_triangleCount[0]];
        var boundaryEdges = new Edge[_boundaryEdgeCount[0]];
        
        _triangleBuffer.GetData(triangles, 0, 0, _triangleCount[0]);
        _boundaryEdgeBuffer.GetData(boundaryEdges, 0, 0, _boundaryEdgeCount[0]);

        _processedEdgeIndices.Clear();
        _edgePaths.Clear();
        _triangleInts.Clear();
        _uniqueVertexIndices.Clear();

        foreach (var tri in triangles)
        {
            if (!_uniqueVertexIndices.ContainsKey(tri.VertexA))
            {
                _uniqueVertexIndices[tri.VertexA] = _uniqueVertexIndices.Count;
            }
            
            if (!_uniqueVertexIndices.ContainsKey(tri.VertexB))
            {
                _uniqueVertexIndices[tri.VertexB] = _uniqueVertexIndices.Count;
            }
            
            if (!_uniqueVertexIndices.ContainsKey(tri.VertexC))
            {
                _uniqueVertexIndices[tri.VertexC] = _uniqueVertexIndices.Count;
            }
            
            _triangleInts.Add(_uniqueVertexIndices[tri.VertexA]);
            _triangleInts.Add(_uniqueVertexIndices[tri.VertexB]);
            _triangleInts.Add(_uniqueVertexIndices[tri.VertexC]);
        }
        
        var go = chunk.GameObject;
        var mesh = chunk.Mesh;
        mesh.Clear();
        mesh.vertices = _uniqueVertexIndices.Keys.ToArray();
        mesh.triangles = _triangleInts.ToArray();
        mesh.RecalculateBounds();
        chunk.Mesh = mesh;

        var trPos = transform.position;
        var qtBounds = new Vector4(trPos.x, trPos.y, resolution, resolution);
        var qt = new EdgeQuadtree(qtBounds);

        foreach (var edge in boundaryEdges)
        {
            qt.Insert(edge);
        }
        
        for (var ei = 0; ei < boundaryEdges.Length; ei++)
        {
            var edge = boundaryEdges[ei];
            
            if (_processedEdgeIndices.ContainsKey(edge)) continue;
        
            var edgeLoop = new List<Vector2> { edge.VertexA, edge.VertexB };
            ProcessEdgePath(edge, edge, edgeLoop);
        
            if (edgeLoop.Count <= 2) continue;
        
            _edgePaths.Add(edgeLoop);
        }
        
        // TODO: figure out a better system for updating edge colliders.
        // Preferably, we should only update the edge colliders that have changed.
        var existingEdgeColliders = go.GetComponents<EdgeCollider2D>();
        for (var i = 0; i < existingEdgeColliders.Length; i++)
        {
            Destroy(existingEdgeColliders[i]);
        }
        
        for (var ei = 0; ei < _edgePaths.Count; ei++)
        {
            var edgeVertices = _edgePaths[ei];
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
        
        // Debug.Log($"Edge loops: {edgePaths.Count}");

        return;

        void ProcessEdgePath(Edge edge, Edge startEdge, List<Vector2> edgePath, bool backwards = false, int depth = 1)
        {
            if (depth == 1)
            {
                _processedEdgeIndices[startEdge] = 0;
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

            var queryPoint = backwards ? edge.VertexA : edge.VertexB;
            var edgesCloseToNext = qt.GetLeafIncludingPoint(queryPoint);

            if (edgesCloseToNext == null)
                throw new NullReferenceException("Quadtree doesn't contain edge.");

            if (depth == 1)
            {
                var edgesCloseToA = qt.GetLeafIncludingPoint(edge.VertexA);
                if (edgesCloseToA != null)
                {
                    edgesCloseToNext.AddRange(edgesCloseToA);
                }
            }

            // recursively find the next edge in the path
            foreach (var otherEdge in edgesCloseToNext)
            {
                if (_processedEdgeIndices.ContainsKey(otherEdge)) continue;
                
                if (edge.VertexA == otherEdge.VertexA) continue;

                // check if the edge is to the relative LEFT of the current edge
                if (depth == 1 || backwards)
                {
                    if (edge.VertexA == otherEdge.VertexB)
                    {
                        _processedEdgeIndices[otherEdge] = 0;

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
                    _processedEdgeIndices[otherEdge] = 0;
                    
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

    private void Terraform(Vector2 localBrushPosition, float brushSize, float strength)
    {
        // Assume the brush position is within the planet terrain area.
        // Assume the brush position is localized to this planet, so that
        // position (res / 2, res / 2) is the center of the planet.
        terraformerCs.SetFloat("brush_x", localBrushPosition.x);
        terraformerCs.SetFloat("brush_y", localBrushPosition.y);
        terraformerCs.SetFloat("brush_size", brushSize);
        terraformerCs.SetFloat("brush_strength", strength);
        terraformerCs.SetFloat("brush_smoothing", 0.2f);
        terraformerCs.Dispatch(0, resolution / 8, resolution / 8, 1);

        if (_buffersInitialized)
        {
            _bufferReleaseTimer = 0;
        }
        else
        {
            InitializeBuffers();
        }
        
        var chunkX = (int)(localBrushPosition.x / resolution * chunksPerAxis);
        var chunkY = (int)(localBrushPosition.y / resolution * chunksPerAxis);
        var sw = new Stopwatch();
        sw.Start();
        ProcessChunk(chunkX, chunkY);
        sw.Stop();
        Debug.Log($"Chunk update took {sw.ElapsedMilliseconds}ms");
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
