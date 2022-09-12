using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;


public class MLS_MPM_part2 : MonoBehaviour
{
    struct Particle
    {
        public float2 x; // position
        public float2 v; // velocity
        public float2x2 C; // affine momentum matrix
        public float mass;
    }

    //struct Cell
    //{
    //    public float2 v; // velocity
    //    public float mass;
    //}

    // simulation parameters
    const int iterations = 10;
    int coef = 10000;

    const int grid_res = 256;
    const int num_cells = grid_res * grid_res;

    const float gravity = -0.3f;

    // fluid parameters
    const float rest_density =4.0f;
    const float dynamic_viscosity = 0.1f;

    // equation of state
    const float eos_stiffness = 10.0f;
    const float eos_power = 4;

    public int num_particles;
    private int kernelClearGrid;
    private int kernelP2Gdensity;
    private int kernelUpdateGrid;
    int kernelParticle2Grid;
    private int kernelGrid2Particle;
    //int kernelAdvection;
    
    int groupSizeX;
    int groupsPerGridCell;

    [SerializeField] Mesh instance_mesh;
    [SerializeField] Material instance_material;

    Particle[] ps;
    //Cell[] grid;
   
    Bounds bounds;

    public ComputeShader shader;
    // Compute buffers used for indirect mesh drawing
    ComputeBuffer point_buffer;
    ComputeBuffer grid_buffer;
    ComputeBuffer args_buffer;

    uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
    
    List<float2> temp_positions;

    void Start()
    {
        kernelClearGrid = shader.FindKernel("ClearGrid");
        kernelP2Gdensity = shader.FindKernel("P2Gdensity");
        kernelParticle2Grid = shader.FindKernel("Particle2Grid");
        kernelUpdateGrid = shader.FindKernel("UpdateGrid");
        kernelGrid2Particle = shader.FindKernel("Grid2Particle");
        //kernelAdvection = shader.FindKernel("Advection");
        
        InitParticles();
        InitShaders();
    }
    void spawn_box(int x, int y, int box_x, int box_y)
    {
        const float spacing = 0.5f;
        for (float i = -box_x / 2; i < box_x / 2; i += spacing)
        {
            for (float j = -box_y / 2; j < box_y / 2; j += spacing)
            {
                var pos = math.float2(x + i, y + j);

                temp_positions.Add(pos);
            }
        }
    }
    
    private void InitParticles()
    {
        // populate our array of particles
        temp_positions = new List<float2>();
        spawn_box(grid_res / 2, grid_res / 2, grid_res / 2, grid_res / 2);
        num_particles = temp_positions.Count;
        Debug.Log(num_cells);

        ps = new Particle[num_particles];

        // initialise particles
        for (int i = 0; i < num_particles; ++i)
        {
            Particle p = new Particle();
            p.x = temp_positions[i];
            //p.v.x = UnityEngine.Random.value * 2.0f - 1.0f;
            //p.v.y = UnityEngine.Random.value * 2.0f - 1.0f;
            p.v = 0;
            p.C = 0;
            p.mass = 1.0f;
            ps[i] = p;

        }

        //grid = new Cell[num_cells];

        //for (int i = 0; i < num_cells; ++i)
        //{
        //    var cell = new Cell();
        //    cell.v = 0;
        //    grid[i] = cell;
        //}
    }
    private void InitShaders() 
    {
        uint x;
        //shader.GetKernelThreadGroupSizes(kernelAdvection, out x, out _, out _);
        shader.GetKernelThreadGroupSizes(kernelParticle2Grid, out x, out _, out _);
        groupSizeX = Mathf.CeilToInt((float)num_particles / (float)x);
        groupsPerGridCell = Mathf.CeilToInt((grid_res * grid_res) / 8f);

        point_buffer = new ComputeBuffer(num_particles, 9 * sizeof(float), ComputeBufferType.Default);
        point_buffer.SetData(ps);
        instance_material.SetBuffer("particle_buffer", point_buffer);

        grid_buffer = new ComputeBuffer(num_cells, 3 * sizeof(int), ComputeBufferType.Default);
        //grid_buffer.SetData(grid);


        // indirect arguments for mesh instances
        args_buffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        uint numIndices = (uint)instance_mesh.GetIndexCount(0);
        args[0] = numIndices;
        args[1] = (uint)point_buffer.count;
        args_buffer.SetData(args);

        // define rendering bounds for DrawMeshInstancedIndirect 
        bounds = new Bounds(Vector3.zero, new Vector3(100, 100, 100));

        shader.SetBuffer(kernelClearGrid, "grid_buffer", grid_buffer);
        shader.SetBuffer(kernelParticle2Grid, "grid_buffer", grid_buffer);
        shader.SetBuffer(kernelParticle2Grid, "particle_buffer", point_buffer);
        shader.SetBuffer(kernelP2Gdensity, "grid_buffer", grid_buffer);
        shader.SetBuffer(kernelP2Gdensity, "particle_buffer", point_buffer);
        shader.SetBuffer(kernelUpdateGrid, "grid_buffer", grid_buffer);
        shader.SetBuffer(kernelGrid2Particle, "grid_buffer", grid_buffer);
        shader.SetBuffer(kernelGrid2Particle, "particle_buffer", point_buffer);

        //shader.SetBuffer(kernelAdvection, "particle_buffer", point_buffer);

        shader.SetInt("grid_res", grid_res);
        shader.SetInt("num_particles", num_particles);
        shader.SetInt("coef", coef);

        shader.SetFloat("rest_density", rest_density);
        shader.SetFloat("dynamic_viscosity", dynamic_viscosity);
        shader.SetFloat("eos_stiffness", eos_stiffness);
        shader.SetFloat("eos_power", eos_power);
        shader.SetFloat("gravity", gravity);
        //shader.SetFloat("deltaTime", dt);

    }

    // Update is called once per frame
    void Update()
    {
        for (int i = 0; i < iterations; ++i)
        {
            shader.SetFloat("deltaTime", Time.deltaTime);
            shader.Dispatch(kernelClearGrid, groupsPerGridCell, 1, 1);
            shader.Dispatch(kernelParticle2Grid, groupSizeX, 1, 1);
            shader.Dispatch(kernelP2Gdensity, groupSizeX, 1, 1);
            shader.Dispatch(kernelUpdateGrid, groupsPerGridCell, 1, 1);
            shader.Dispatch(kernelGrid2Particle, groupSizeX, 1, 1);
            //shader.Dispatch(kernelAdvection, groupSizeX, 1, 1);
        }
        Graphics.DrawMeshInstancedIndirect(instance_mesh, 0, instance_material, bounds, args_buffer);
    }
    void OnDestroy()
    {
        if (point_buffer != null)
        {
            point_buffer.Dispose();
        }

        if (args_buffer != null)
        {
            args_buffer.Dispose();
        }

        if (grid_buffer != null)
        {
            grid_buffer.Dispose();
        }
    }

}
