using System;
using System.Drawing;
using System.Collections.Generic;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using OpenTK.Input;

namespace Ropesim
{
    ///<summary>
    /// class utilizing the OpenTK Gameview class to visualize spring using OpenGL.
    ///     -Press Left mouse button to apply velocity to spring
    ///     -Press Right mouse button to switch render mode
    ///</summary>
    class Renderer
    {
        private static int            startTime = 0;
        private static RopeSimulation rope = new RopeSimulation(12, 0.01f, 20f, 4f, 25, 0.5f, new Vector2(0, 4));
        private static Random         rnd  = new Random();

        public enum RenderMode
        {
            Line,
            Triangle,
            Point,
            Wireframe
        }
        private static RenderMode     m_renderMode = RenderMode.Line;

        //Get time since start
        private static float time
        {
           get { return Environment.TickCount / 1000.0f; }
        }


        [STAThread]
        public static void Main()
        {
            float lastTime = time;

            using (var game = new GameWindow())
            {
                game.Load += (sender, e) =>
                {
                    game.VSync = VSyncMode.On;
                    startTime  = Environment.TickCount;
                };

                //Apply velocity to spring when left mouse button is pressed
                game.MouseDown += (sender, e) =>
                {
                    if (e.Button == MouseButton.Left)
                    {
                        Vector2 v = new Vector2(-game.Width * 0.5f + game.Mouse.X, -game.Height * 0.5f + game.Mouse.Y);

                        rope.AddVelocity(v);
                    }
                };

                //Switch rendermode when right mouse button is pressed
                game.MouseDown += (sender, e) =>
                {
                    if (e.Button == MouseButton.Right)
                    {
                        switch(m_renderMode)
                        {
                            case RenderMode.Line:       m_renderMode = RenderMode.Point;        break;
                            case RenderMode.Point:      m_renderMode = RenderMode.Triangle;     break;
                            case RenderMode.Triangle:   m_renderMode = RenderMode.Wireframe;    break;
                            case RenderMode.Wireframe:  m_renderMode = RenderMode.Line;         break;
                        }
                    }
                };

                game.Resize += (sender, e) =>
                {
                    GL.Viewport(0, 0, game.Width, game.Height);
                };

                game.UpdateFrame += (sender, e) =>
                {

                    float curTime = time;
                    float delta = curTime - lastTime;
                          delta = Math.Max(0.01f, delta); //Min Frame deltatime
                    lastTime = curTime;

                    //Dont simulate whe not focused
                    if (game.Focused)
                    {
                        //Display fps in the title
                        game.Title = "Rope Simulation FPS : " + (1 / delta).ToString();

                        rope.Update(delta * 4, new Vector2(0, -20));

                        // demo controls
                        if (game.Keyboard[Key.Escape])
                        {
                            game.Exit();
                        }

                    }
                };

                game.RenderFrame += (sender, e) =>
                {
                    GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

                    #region PROJECTION MATRIX
                    float fov       = MathHelper.DegreesToRadians(90);
                    float aspect    = game.Width / (float)game.Height;
                    float zNear     = 0.1f;
                    float zFar      = 500;
                    var projMat     =                     Matrix4.CreateOrthographic(25, 25, 0.1f, 500);
                    GL.MatrixMode(MatrixMode.Projection);
                    GL.LoadMatrix(ref projMat);
                    #endregion

                    #region CAMERA MATRIX
                    GL.MatrixMode(MatrixMode.Modelview);
                    var lookMat     = Matrix4.LookAt(new Vector3(0,0,-10), new Vector3(0,0,0), Vector3.UnitY);
                    var modelMat    = Matrix4.CreateRotationY(0);
                    lookMat         = modelMat * lookMat;
                    GL.LoadMatrix(ref lookMat);
                    #endregion

                    #region RENDERING
                    
                    switch(m_renderMode)
                    {
                        case RenderMode.Wireframe:
                            GL.Begin(PrimitiveType.Lines);

                            GL.Color4(Color.Cyan);

                            for (int i = 0; i < rope.m_triangles.Length; i += 3)
                            {
                                GL.Vertex3(rope.m_vertices[rope.m_triangles[i]]);
                                GL.Vertex3(rope.m_vertices[rope.m_triangles[i +1]]);

                                GL.Vertex3(rope.m_vertices[rope.m_triangles[i +1]]);
                                GL.Vertex3(rope.m_vertices[rope.m_triangles[i +2]]);

                                GL.Vertex3(rope.m_vertices[rope.m_triangles[i +2]]);
                                GL.Vertex3(rope.m_vertices[rope.m_triangles[i]]);
                            }

                            break;

                        case RenderMode.Line:
                            GL.Begin(PrimitiveType.Lines);

                            GL.Color4(Color.Magenta);

                            for(int i = 1; i < rope.particles.Length; ++i)
                            {
                                GL.Vertex3(rope.particles[i - 1].State3D);
                                GL.Vertex3(rope.particles[i].State3D);
                            }

                            break;

                        case RenderMode.Point:
                            GL.Begin(PrimitiveType.Points);

                            GL.Color4(Color.Green);

                            for (int i = 0; i < rope.m_vertices.Length; ++i)
                                GL.Vertex3(rope.m_vertices[i]);

                            break;

                        case RenderMode.Triangle:
                            GL.Begin(PrimitiveType.Triangles);

                            GL.Color4(Color.Yellow);

                            for (int i = 0; i < rope.m_triangles.Length; ++i)
                                GL.Vertex3(rope.m_vertices[rope.m_triangles[i]]);

                            break;

                    }

                    GL.End();
                    #endregion

                    game.SwapBuffers();
                };

                game.Run(60.0);
            }
        }
    }

    /// <summary>
    /// A Basic Rope simulation class 
    ///     -Based on my previous experience with procedural meshes, cloth physics and spring physics.
    ///     -Vertices are connected by springs which inherit their reststates from previous vertices in the spring
    ///     -rope is then converted into a simple polygon to give it some width
    ///     -rope parameters set in the renderer class are not physically accurate
    ///     -known issue: if framerate gets really low damping calculation will invert spring velocity (velocity *= 1 - damping * deltatime)
    /// </summary>
    public class RopeSimulation
    {
        public class Particle
        {
            public float    restLength;
            public bool     pinned = false;

            internal Vector2  restState;
            internal Vector2  state;
            internal Vector2  velocity;

            private float stiffness;
            private float damping;
            private float maxVelocity;

            private Particle parent;

            //Type constructor
            public Particle (float restLength0, float stiffness0, float damping0, float maxVelocity0, Particle parent0, Vector2 offset)
            {
                parent      = parent0;
                restLength  = restLength0;
                stiffness   = stiffness0;
                damping     = damping0;
                maxVelocity = maxVelocity0;

                //Dont add rest length if the particle has no parent
                restState   = offset + (parent != null? parent.state - new Vector2( 0, restLength) : Vector2.Zero);
                state       = restState;
                velocity    = Vector2.Zero;
            }

            public  void    Update(Vector2 gravity, float delta)
            {
                //Dont do anything if the particle is pinned
                if(pinned)
                    return;

                //Calculate equilibrium state
                restState = parent.state + (state - parent.state).Normalized() * restLength;

                //Add force towards rest state
                velocity += (restState - state) * delta * stiffness;

                //Add gravity force
                velocity += gravity * delta;

                //Damp velocity
                velocity *= 1 - (damping * delta);

                //Clamp velocity
                velocity  = ClampMagnitude(velocity, maxVelocity);
                
                //Add velocity to state
                state += velocity * delta;

                //Set velocity to zero when small enough
                if (velocity.LengthSquared < 0.001f)
                    velocity = Vector2.Zero;
            }
            
            //Reset spring (unused)
            public  void    Reset()
            {
                if (pinned)
                    return;

                restState = parent.state + (state - parent.state).Normalized() * restLength;
                state     = restState;
                velocity  = Vector2.Zero;
            }

            //Clamp vector2 magnitude
            private Vector2 ClampMagnitude(Vector2 v, float mag)
            {
                float m = v.Length;

                m = m > mag ? mag : m;

                return v.Normalized() * m;
            }

            //Normal of reststate - state
            public  Vector3 normal
            {
                get
                {
                    if (pinned)
                        return new Vector3(1, 0, 0);

                    Vector2 v = state - restState;

                    v = new Vector2(-v.Y, v.X);

                    v.Normalize();

                    return  new Vector3(v.X, v.Y, 0);
                }
            }    

            //Convert state to Vector3
            public  Vector3 State3D
            {
                get { return new Vector3(state.X, state.Y, 0); }
            }
        }

        internal Particle[] particles;

        internal Vector3[] m_vertices;
        internal int[]     m_triangles;
        private  float     m_thickness;

        //Type constructor
        public RopeSimulation(int points, float restlength, float stiffness, float damping, float maxVelocity, float thickness, Vector2 offset)
        {
            m_thickness = thickness;

            particles = new Particle[points];

            particles[0]        = new Particle(10, 10, 10, 10, null, offset);
            particles[0].pinned = true;

            for (int i = 1; i < particles.Length; ++i) particles[i] = new Particle(restlength, stiffness, damping, maxVelocity, particles[i - 1], offset);

            m_vertices = new Vector3[points * 2];

            List<int> temp = new List<int>();

            for(int i = 0; i < points -1; ++i)
            {
                //Clockwise
                temp.Add(i);
                temp.Add(i + points);
                temp.Add(i + 1);

                temp.Add(i + points);
                temp.Add(i + points + 1);
                temp.Add(i + 1);
            }

            m_triangles = temp.ToArray();
        }

        public  void Update(float delta, Vector2 gravity)
        {
            //Update each spring starting from the top
           for(int i = 0; i < particles.Length; ++i)
                particles[i].Update(gravity, delta);

           //Update mesh vertices
            UpdateVertices();
        }
        private void UpdateVertices()
        {
            Vector3 v;
            Vector3 p;

            
            for (int i = 0; i < particles.Length; ++i)
            {
                v = particles[i].normal * m_thickness;
                p = particles[i].State3D;

                m_vertices[i] = p + v;
                m_vertices[i + particles.Length] = p - v;
               
            }

        }

        //Add Velocity to spring with linear 0-1 weight from top to bottom
        public  void AddVelocity(Vector2 vel)
        {
            float weight;

            for(int i = 0; i < particles.Length; ++i)
            {
                weight  = (float)i / particles.Length;
                particles[i].velocity += vel * weight;
            }
        }
    }
}