// File author: Tuukka Takala
using System.Collections.Generic;
using Unity.Entities;
using Unity.Rendering;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public class TerrainGenerator : MonoBehaviour
{
    [Header("Terrain Settings")]
    public int width = 128;
    public int height = 128;
    public int depth = 64;
    public float dirtScale = 10.0f; // I separated ChatGPT's original scale to dirtScale and rockScale
    public float rockScale = 10.0f;
    [Range(0f, 1f)]
    public float dirtAmount = 1f; // I added this
    [Range(0f, 1f)]
    public float rockAmount = 1f; // I added this

    [SerializeField] private Mesh cubeMesh;

    [Header("Material Slots")]
    public Material dirtMaterial;
    public Material rockMaterial;
    public Material waterMaterial;
    public Material goldMaterial;
    public Material foliageMaterial;
    public Material woodMaterial;
    public Material leavesMaterial;

    Entity dirt = Entity.Null;
    Entity rock = Entity.Null;
    Entity water = Entity.Null;
    Entity gold = Entity.Null;
    Entity foliage = Entity.Null;
    Entity wood = Entity.Null;
    Entity leaves = Entity.Null;

    private EntityManager entityManager;
    private EntityArchetype cubeArchetype;

    void Start()
    {
        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        cubeArchetype = entityManager.CreateArchetype(
            typeof(RenderMesh),
            typeof(RenderBounds),
            typeof(LocalToWorld)
        );

        GenerateTerrain();
    }

    void GenerateTerrain()
    {
        Pass1_CreateBasicTerrain();
        Pass2_PerlinWorms();
        Pass3_AdditionalFeatures();
    }

    void Pass1_CreateBasicTerrain()
    {
        // Set up Perlin noise seeds
        float dirtSeed = UnityEngine.Random.Range(0, 10000);
        float rockSeed = UnityEngine.Random.Range(0, 10000);

        // Create the basic terrain using Perlin noise
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                // Sample Perlin noise for dirt and rock
                float dirtHeight = depth * Mathf.Pow(Mathf.PerlinNoise((x + dirtSeed) / dirtScale, (z + dirtSeed) / dirtScale) 
                                        * Mathf.PerlinNoise((x - rockSeed) / dirtScale, (z - rockSeed) / dirtScale), 2 - 1.5f * dirtAmount);
                float rockHeight = depth * Mathf.Pow(Mathf.PerlinNoise((x + rockSeed) / rockScale, (z + rockSeed) / rockScale) 
                                        * Mathf.PerlinNoise((x - dirtSeed) / rockScale, (z - dirtSeed) / rockScale), 2 - 1.5f * rockAmount);
                // I added the second PerlinNoise terms to increase height variability and the Pow-functions with dirtAmount and rockAmount

                for (int y = 0; y < depth; y++)
                {
                    float3 position = new float3(x, y, z);
                    Material material = null;

                    if (y <= dirtHeight)
                    {
                        material = dirtMaterial;
                    }
                    else if (y <= rockHeight)
                    {
                        material = rockMaterial;
                    }

                    if (material != null)
                    {
                        CreateCube(material, position.x, position.y, position.z);
                    }
                }
            }
        }
    }


    [Header("Water Settings")]
    public int waterSources = 20;
    public int waterLevel = 10;

    private Dictionary<Vector3Int, Entity> terrainCubes;

    void FloodFillWater(Vector3Int source)
    {
        Queue<Vector3Int> queue = new Queue<Vector3Int>();
        HashSet<Vector3Int> visited = new HashSet<Vector3Int>();

        queue.Enqueue(source);
        visited.Add(source);

        while (queue.Count > 0)
        {
            Vector3Int current = queue.Dequeue();
            if (!terrainCubes.ContainsKey(current) && current.y <= waterLevel)
            {
                Entity waterCube = CreateCube(waterMaterial, current.x, current.y, current.z);
                Vector3Int position = new Vector3Int(current.x, current.y, current.z);
                terrainCubes[position] = waterCube;
                // Below is ChatGPT's solution, which didn't include any boundaries for the flood fill
                // Check all 6-connected neighbors
                //Vector3Int[] neighbors = new Vector3Int[]
                //{
                //    current + Vector3Int.right,
                //    current + Vector3Int.left,
                //    current + Vector3Int.up,
                //    current + Vector3Int.down,
                //    current + Vector3Int.forward,
                //    current + Vector3Int.back
                //};
                List<Vector3Int> neighbors = new List<Vector3Int>();
                if (current.x < width-1)
                    neighbors.Add(current + Vector3Int.right);
                if (current.x > 0)
                    neighbors.Add(current + Vector3Int.left);
                if (current.z < height-1)
                    neighbors.Add(current + Vector3Int.forward);
                if (current.z > 0)
                    neighbors.Add(current + Vector3Int.back);
                if (current.z < depth - 1)
                    neighbors.Add(current + Vector3Int.up);
                if (current.z > 0)
                    neighbors.Add(current + Vector3Int.down);

                foreach (Vector3Int neighbor in neighbors)
                {
                    if (!visited.Contains(neighbor))
                    {
                        queue.Enqueue(neighbor);
                        visited.Add(neighbor);
                    }
                }
            }
        }
    }

    [Header("Perlin Worm Settings")]
    public int numberOfWorms = 10;
    public float wormMinRadius = 3.0f; // I split ChatGPT's wormRadius into two
    public float wormMaxRadius = 30.0f; // I split ChatGPT's wormRadius into two
    public int wormLength = 100;
    public float wormStep = 0.1f;

    void Pass2_PerlinWorms()
    {

        terrainCubes = new Dictionary<Vector3Int, Entity>();
        EntityQuery cubeQuery = entityManager.CreateEntityQuery(typeof(Translation));

        // Add existing cubes to the dictionary
        foreach (var cube in cubeQuery.ToEntityArray(Unity.Collections.Allocator.Temp))
        {
            // Had to change ChatGPTs FloorToInt into RoundToInt because floats can be 2.99999 or 3.00000
            Vector3Int position = Vector3Int.RoundToInt(entityManager.GetComponentData<Translation>(cube).Value);
            terrainCubes[position] = cube;
        }

        for (int i = 0; i < numberOfWorms; i++)
        {
            float wormX = UnityEngine.Random.Range(0, width);
            float wormY = UnityEngine.Random.Range(depth * 0.25f, depth * 0.75f);
            float wormZ = UnityEngine.Random.Range(0, height);

            Vector3 wormPosition = new Vector3(wormX, wormY, wormZ);

            for (int j = 0; j < wormLength; j++)
            {
                CarveWorm(wormPosition, j);

                // Update the worm's position using Perlin noise
                float wormDirectionX = Mathf.PerlinNoise(wormX * wormStep, 0) * 2 - 1;
                float wormDirectionY = Mathf.PerlinNoise(0, wormY * wormStep) * 2 - 1;
                float wormDirectionZ = Mathf.PerlinNoise(wormZ * wormStep, 0) * 2 - 1;

                wormPosition += new Vector3(wormDirectionX, wormDirectionY, wormDirectionZ);
            }
        }

        // At the moment it's not checked whether carving creates floating islands 

        // Add water sources
        for (int i = 0; i < waterSources; i++)
        {
            int x = UnityEngine.Random.Range(0, width);
            int z = UnityEngine.Random.Range(0, height);
            int y = UnityEngine.Random.Range(0, waterLevel);

            Vector3Int waterSource = new Vector3Int(x, y, z);
            FloodFillWater(waterSource);
        }
    }

    void CarveWorm(Vector3 position, float wormSegment)
    {
        // Had to change ChatGPTs FloorToInt into RoundToInt because floats can be 2.99999 or 3.00000
        Vector3Int min = Vector3Int.RoundToInt(position - new Vector3(wormMaxRadius, wormMinRadius, wormMaxRadius));
        Vector3Int max = Vector3Int.RoundToInt(position + new Vector3(wormMaxRadius, wormMinRadius, wormMaxRadius));
        
        // Added tapering at both ends of the worm
        float endTaper = 1;
        float averageRadius = 0.5f * (wormMinRadius + wormMaxRadius);
        if (wormSegment < averageRadius)
            endTaper = wormSegment / averageRadius;
        if (wormLength - wormSegment < averageRadius)
            endTaper = (wormLength - wormSegment) / averageRadius;
        endTaper = Mathf.Sqrt(endTaper);
        float varyingRadius = 1.0f; // I added this
        for (int x = min.x; x <= max.x; x++)
        {
            for (int y = min.y; y <= max.y; y++)
            {
                for (int z = min.z; z <= max.z; z++)
                {
                    // Hacked ChatGPT's sphere to ellipsoid whose horizontal radii grows from wormMinRadius to wormMaxRadius along y (height)
                    float normalizedHeight = ((float)y) / ((float)depth);
                    varyingRadius = (wormMaxRadius*normalizedHeight + (1-normalizedHeight)*wormMinRadius) * endTaper;
                    Vector3Int cubePosition = new Vector3Int(x, y, z);
                    Vector3 difference = position - new Vector3(cubePosition.x, cubePosition.y, cubePosition.z);
                    float normalizedDistance = (difference.x * difference.x + difference.z * difference.z) / (varyingRadius * varyingRadius) 
                                                + difference.y * difference.y;

                    //float distance = Vector3.Distance(position, cubePosition);
                    
                    if (/*distance <= varyingRadius*/ normalizedDistance < 1 && terrainCubes.ContainsKey(cubePosition))
                    {
                        entityManager.DestroyEntity(terrainCubes[cubePosition]);
                        terrainCubes.Remove(cubePosition);
                    }
                }
            }
        }
    }

    [Header("Gold Settings")]
    public float goldProbability = 0.01f;
    public float goldScale = 10.0f;

    [Header("Foliage Settings")]
    public float foliageProbability = 0.9f;
    public float foliageScale = 10f;

    [Header("Tree Settings")]
    public int minTreeHeight = 4;
    public int maxTreeHeight = 8;
    public float treeProbability = 0.1f;
    public float leavesRadius = 2.0f;

    void Pass3_AdditionalFeatures()
    {
        float goldSeed = UnityEngine.Random.Range(0, 10000);
        float foliageSeed = UnityEngine.Random.Range(0, 10000);

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                int topDirtY = -1;

                for (int y = 0; y < depth; y++)
                {
                    Vector3Int position = new Vector3Int(x, y, z);

                    if (terrainCubes.ContainsKey(position))
                    {
                        Entity cube = terrainCubes[position];
                        Material material = entityManager.GetSharedComponentData<RenderMesh>(cube).material;

                        // Add gold underground
                        if (material == rockMaterial)
                        {
                            float goldNoise = Mathf.PerlinNoise((x + goldSeed) / goldScale, (z + goldSeed) / goldScale);
                            if (goldNoise < goldProbability)
                            {
                                entityManager.DestroyEntity(terrainCubes[position]);
                                terrainCubes.Remove(position);
                                Entity goldCube = CreateCube(goldMaterial, x, y, z);
                                terrainCubes[position] = goldCube;
                                //entityManager.SetSharedComponentData(cube, new RenderMesh
                                //{
                                //    mesh = cubeMesh,
                                //    material = goldMaterial,
                                //    receiveShadows = false
                                //});
                            }
                            topDirtY = -1; // I had to add this so that foliage wouldn't appear inside of rock
                        }
                        // Find topmost dirt cube
                        else if (material == dirtMaterial)
                        {
                            topDirtY = y;
                        }
                    }
                }

                // Add foliage and trees above the topmost dirt cube
                if (topDirtY != -1)
                {
                    //float foliageRoll = UnityEngine.Random.value; // Switched this ChatGPT solution to Perlin Noise + Random
                    float foliageRoll = Mathf.PerlinNoise((x + foliageSeed)/foliageScale, (z + foliageSeed)/ foliageScale) 
                        + 0.5f*foliageProbability*(UnityEngine.Random.value);
                    if (foliageRoll <= foliageProbability && !terrainCubes.ContainsKey(new Vector3Int(x, topDirtY + 1, z)))
                    {
                        CreateCube(foliageMaterial, x, topDirtY + 1, z);
                    }
                    float treeRoll = UnityEngine.Random.value;
                    if (treeRoll <= treeProbability && !terrainCubes.ContainsKey(new Vector3Int(x, topDirtY + 2, z)))
                    {
                        int treeHeight = UnityEngine.Random.Range(minTreeHeight, maxTreeHeight);
                        Vector3 treeBasePosition = new Vector3(x, topDirtY + 2, z);
                        CreateTree(treeBasePosition, treeHeight);
                    }
                }
            }
        }
    }


    void CreateTree(Vector3 basePosition, int height)
    {
        // Create tree trunk
        for (int i = 0; i < height; i++)
        {
            CreateCube(woodMaterial, basePosition.x, i+basePosition.y, basePosition.z);
        }

        // Create tree leaves in a spherical shape
        Vector3 leavesCenter = basePosition + new Vector3(0, height, 0);

        for (int x = -Mathf.CeilToInt(leavesRadius); x <= Mathf.CeilToInt(leavesRadius); x++)
        {
            for (int y = -Mathf.CeilToInt(leavesRadius); y <= Mathf.CeilToInt(leavesRadius); y++)
            {
                for (int z = -Mathf.CeilToInt(leavesRadius); z <= Mathf.CeilToInt(leavesRadius); z++)
                {
                    Vector3 offset = new Vector3(x, y, z);
                    Vector3 currentPos = leavesCenter + offset;
                    float distance = Vector3.Distance(leavesCenter, currentPos);

                    if (distance <= leavesRadius)
                    {
                        CreateCube(leavesMaterial, currentPos.x, currentPos.y, currentPos.z);
                    }
                }
            }
        }
    }

    // This was added because ChatGPT's solution didn't work with Entities package 0.51 (also it didn't instantiate)
    Entity CreateCube(Material pieceMaterial, float x, float y, float z)
    {
        Entity piece = Entity.Null;
        bool newPrototypeCreated = false;
        float vertNoise = 0; // I added this for creating a bit of a gap between certain cubes via rotation
        Quaternion pieceRotation = Quaternion.identity; // I added this for the same reason
        if (pieceMaterial == rockMaterial)
        {
            if (rock == Entity.Null)
            {
                rock = entityManager.CreateEntity(cubeArchetype);
                newPrototypeCreated = true;
            }
            piece = rock;
            pieceRotation = Quaternion.Euler(0, 10, 0);
            vertNoise = UnityEngine.Random.Range(-0.2f, 0.2f);
        }
        if (pieceMaterial == dirtMaterial)
        {
            if (dirt == Entity.Null)
            {
                dirt = entityManager.CreateEntity(cubeArchetype);
                newPrototypeCreated = true;
            }
            piece = dirt;
            pieceRotation = Quaternion.Euler(1, 0, 1);
            vertNoise = UnityEngine.Random.Range(-0.1f, 0.1f);
        }
        if (pieceMaterial == goldMaterial)
        {
            if (gold == Entity.Null)
            {
                gold = entityManager.CreateEntity(cubeArchetype);
                newPrototypeCreated = true;
            }
            piece = dirt;
        }
        if (pieceMaterial == foliageMaterial)
        {
            if (foliage == Entity.Null)
            {
                foliage = entityManager.CreateEntity(cubeArchetype);
                newPrototypeCreated = true;
            }
            piece = foliage;
            pieceRotation = Quaternion.Euler(3, 0, 3);
            vertNoise = UnityEngine.Random.Range(-0.1f, 0.1f);
        }
        if (pieceMaterial == waterMaterial)
        {
            if (water == Entity.Null)
            {
                water = entityManager.CreateEntity(cubeArchetype);
                newPrototypeCreated = true;
            }
            piece = water;
        }
        if (pieceMaterial == woodMaterial)
        {
            if (wood == Entity.Null)
            {
                wood = entityManager.CreateEntity(cubeArchetype);
                newPrototypeCreated = true;
            }
            piece = wood;
        }
        if (pieceMaterial == leavesMaterial)
        {
            if (leaves == Entity.Null)
            {
                leaves = entityManager.CreateEntity(cubeArchetype);
                newPrototypeCreated = true;
            }
            piece = leaves;
            pieceRotation = Quaternion.Euler(0, 45, 0);
        }

        if (newPrototypeCreated)
        {
            //piece = entityManager.CreateEntity(terrainArchetype);
            //Material material = y < height * waterLevel ? rockMaterial : dirtMaterial;
            var desc = new RenderMeshDescription(cubeMesh, pieceMaterial, UnityEngine.Rendering.ShadowCastingMode.On, true);
            RenderMeshUtility.AddComponents(piece, entityManager, desc);
            entityManager.AddComponentData(piece, new Rotation { Value = pieceRotation }); // Added this to create "texture" between cubes
            entityManager.AddComponentData(piece, new Translation { Value = new float3(x, y + vertNoise, z) }); // Added for same reason
            //print("created " + pieceMaterial);
            return piece;
        }
        else
        {
            var secondEntity = entityManager.Instantiate(piece);
            entityManager.SetComponentData(secondEntity, new Translation { Value = new float3(x, y + vertNoise, z) });
            return secondEntity;
        }
    }
}

