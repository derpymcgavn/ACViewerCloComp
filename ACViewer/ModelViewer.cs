using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

using MonoGame.Framework.WpfInterop.Input;

using ACE.DatLoader;
using ACE.DatLoader.FileTypes;
using ACE.DatLoader.Entity; // added for CloSubPalette
using ACE.Entity.Enum;

using ACE.Server.Physics.Animation;

using ACViewer.Config;
using ACViewer.Enum;
using ACViewer.Model;
using ACViewer.Render;
using ACViewer.View;

namespace ACViewer
{
    public class ModelViewer
    {
        public static GraphicsDevice GraphicsDevice => GameView.Instance.GraphicsDevice;

        public static MainWindow MainWindow => MainWindow.Instance;

        public static ModelViewer Instance { get; set; }

        public SetupInstance Setup { get; set; }
        public R_EnvCell EnvCell { get; set; }
        public R_Environment Environment { get; set; }

        public static Effect Effect => Render.Render.Effect;
        public static Effect Effect_Clamp => Render.Render.Effect_Clamp;

        public WpfKeyboard Keyboard => GameView.Instance._keyboard;
        public WpfMouse Mouse => GameView.Instance._mouse;

        public KeyboardState PrevKeyboardState => GameView.Instance.PrevKeyboardState;

        public static Camera Camera => GameView.Camera;

        public ViewObject ViewObject { get; set; }

        public bool GfxObjMode { get; set; }

        public ModelType ModelType { get; set; }

        // === Reflection / Ground Plane Support ===
        public static bool ShowReflectionPlane { get; set; } = true;   // toggle-able later via options
        private BasicEffect _groundEffect;
        private VertexBuffer _groundVB;
        private IndexBuffer _groundIB;
        private float _groundZ;
        private float _groundSize;
        private uint _groundSetupId; // track if setup changed

        private void EnsureGroundResources()
        {
            if (!ShowReflectionPlane || Setup == null) return;
            var id = Setup.Setup._setup.Id;
            if (_groundVB != null && _groundSetupId == id) return;

            _groundSetupId = id;

            Vector3 min = new Vector3(float.MaxValue);
            Vector3 max = new Vector3(float.MinValue);
            bool found = false;
            try
            {
                var bb = Setup.Setup.BoundingBox;
                var t = bb.GetType();
                var minProp = t.GetProperty("Min");
                var maxProp = t.GetProperty("Max");
                if (minProp != null && maxProp != null)
                {
                    var minVal = (Vector3)minProp.GetValue(bb);
                    var maxVal = (Vector3)maxProp.GetValue(bb);
                    min = minVal; max = maxVal; found = true;
                }
            }
            catch { }

            if (!found)
            {
                var verts = Setup.Setup.GetVertices();
                foreach (var p in verts)
                {
                    if (p.X < min.X) min.X = p.X; if (p.Y < min.Y) min.Y = p.Y; if (p.Z < min.Z) min.Z = p.Z;
                    if (p.X > max.X) max.X = p.X; if (p.Y > max.Y) max.Y = p.Y; if (p.Z > max.Z) max.Z = p.Z;
                }
                if (verts.Count == 0)
                {
                    min = new Vector3(-0.5f); max = new Vector3(0.5f);
                }
            }

            _groundZ = min.Z;
            _groundSize = Math.Max(Math.Max(max.X - min.X, max.Y - min.Y), (max.Z - min.Z)) * 2f;
            if (_groundSize <= 0) _groundSize = 1f;

            var color = new Color(64, 64, 72, 160);
            var vertsPlane = new VertexPositionColor[]
            {
                new VertexPositionColor(new Vector3(-_groundSize, -_groundSize, _groundZ), color),
                new VertexPositionColor(new Vector3( _groundSize, -_groundSize, _groundZ), color),
                new VertexPositionColor(new Vector3( _groundSize,  _groundSize, _groundZ), color),
                new VertexPositionColor(new Vector3(-_groundSize,  _groundSize, _groundZ), color)
            };
            _groundVB = new VertexBuffer(GraphicsDevice, typeof(VertexPositionColor), vertsPlane.Length, BufferUsage.WriteOnly);
            _groundVB.SetData(vertsPlane);
            var indices = new ushort[] { 0,1,2, 2,3,0 };
            _groundIB = new IndexBuffer(GraphicsDevice, IndexElementSize.SixteenBits, indices.Length, BufferUsage.WriteOnly);
            _groundIB.SetData(indices);

            _groundEffect ??= new BasicEffect(GraphicsDevice)
            {
                LightingEnabled = false,
                VertexColorEnabled = true,
                TextureEnabled = false,
                PreferPerPixelLighting = false,
                FogEnabled = false
            };
        }

        private void DrawGroundPlane()
        {
            if (!ShowReflectionPlane || _groundVB == null || _groundIB == null) return;

            _groundEffect.World = Matrix.Identity;
            _groundEffect.View = Camera.ViewMatrix;
            _groundEffect.Projection = Camera.ProjectionMatrix;

            foreach (var pass in _groundEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                GraphicsDevice.SetVertexBuffer(_groundVB);
                GraphicsDevice.Indices = _groundIB;
                GraphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, 2);
            }
        }

        private void DrawReflection()
        {
            if (!ShowReflectionPlane || Setup == null || ViewObject == null) return;
            var world = Matrix.CreateScale(1f, 1f, -1f) * Matrix.CreateTranslation(0f, 0f, 2f * _groundZ);
            Effect.Parameters["xWorld"].SetValue(world);
            Effect_Clamp.Parameters["xWorld"].SetValue(world);

            var prevBlend = GraphicsDevice.BlendState;
            var prevRaster = GraphicsDevice.RasterizerState;
            var prevDepth = GraphicsDevice.DepthStencilState;

            GraphicsDevice.BlendState = BlendState.AlphaBlend;
            GraphicsDevice.RasterizerState = new RasterizerState { CullMode = Microsoft.Xna.Framework.Graphics.CullMode.None };
            GraphicsDevice.DepthStencilState = DepthStencilState.Default;

            Effect.Parameters["xOpacity"].SetValue(0.45f);
            Effect_Clamp.Parameters["xOpacity"].SetValue(0.45f);

            Setup.Draw(PolyIdx, PartIdx);

            Effect.Parameters["xWorld"].SetValue(Matrix.Identity);
            Effect_Clamp.Parameters["xWorld"].SetValue(Matrix.Identity);
            Effect.Parameters["xOpacity"].SetValue(1.0f);
            Effect_Clamp.Parameters["xOpacity"].SetValue(1.0f);

            GraphicsDevice.BlendState = prevBlend;
            GraphicsDevice.RasterizerState = prevRaster;
            GraphicsDevice.DepthStencilState = prevDepth;
        }
        // === End Reflection Support ===

        public ModelViewer()
        {
            Instance = this;
        }

        public void LoadModel(uint id)
        {
            TextureCache.Init();

            // can be either a gfxobj or setup id
            // if gfxobj, create a simple setup
            MainWindow.Status.WriteLine($"Loading {id:X8}");
            GfxObjMode = id >> 24 == 0x01;

            Setup = new SetupInstance(id);
            InitObject(id);

            Camera.InitModel(Setup.Setup.BoundingBox);
            EnsureGroundResources();
            ModelType = ModelType.Setup;
        }

        /// <summary>
        /// Load a model with a ClothingTable
        /// </summary>
        public void LoadModel(uint setupID, ClothingTable clothingBase, PaletteTemplate paletteTemplate, float shade)
        {
            TextureCache.Init();

            // assumed to be in Setup mode for ClothingBase
            GfxObjMode = false;

            // CHANGED: build ObjDesc from in-memory clothing table so runtime texture overrides are honored
            var objDesc = new ACViewer.Model.ObjDesc(setupID, clothingBase, paletteTemplate, shade);

            Setup = new SetupInstance(setupID, objDesc);

            if (ViewObject == null || ViewObject.PhysicsObj.PartArray.Setup._dat.Id != setupID)
            {
                InitObject(setupID);
                Camera.InitModel(Setup.Setup.BoundingBox);
            }
            EnsureGroundResources();
            ModelType = ModelType.Setup;
        }

        // added custom palette loader
        public void LoadModelCustom(uint setupID, ClothingTable clothingBase, List<CloSubPalette> customSubPalettes, float shade)
        {
            TextureCache.Init();
            GfxObjMode = false;

            // Use the ClothingTable instance directly so runtime texture overrides are honored
            var objDesc = new ACViewer.Model.ObjDesc(setupID, clothingBase, PaletteTemplate.Undef, shade);

            if (customSubPalettes != null && customSubPalettes.Count > 0)
            {
                objDesc.PaletteChanges = new PaletteChanges(customSubPalettes, shade);
            }
            Setup = new SetupInstance(setupID, objDesc);
            if (ViewObject == null || ViewObject.PhysicsObj.PartArray.Setup._dat.Id != setupID)
            {
                InitObject(setupID);
                Camera.InitModel(Setup.Setup.BoundingBox);
            }
            EnsureGroundResources();
            ModelType = ModelType.Setup;
        }

        public void LoadEnvironment(uint envID)
        {
            ViewObject = null;
            Environment = new R_Environment(envID);

            ModelType = ModelType.Environment;
        }

        public void LoadEnvCell(uint envCellID)
        {
            var envCell = new ACE.Server.Physics.Common.EnvCell(DatManager.CellDat.ReadFromDat<EnvCell>(envCellID));
            envCell.Pos = new ACE.Server.Physics.Common.Position();
            EnvCell = new R_EnvCell(envCell);

            ModelType = ModelType.EnvCell;
        }

        public void LoadScript(uint scriptID)
        {
            var createParticleHooks = ParticleViewer.Instance.GetCreateParticleHooks(scriptID, 1.0f);

            ViewObject.PhysicsObj.destroy_particle_manager();
            
            foreach (var createParticleHook in createParticleHooks)
            {
                ViewObject.PhysicsObj.create_particle_emitter(createParticleHook.EmitterInfoId, (int)createParticleHook.PartIndex, new AFrame(createParticleHook.Offset), (int)createParticleHook.EmitterId);
            }
        }

        public void InitObject(uint setupID)
        {
            ViewObject = new ViewObject(setupID);

            if (Setup.Setup._setup.DefaultScript != 0)
                LoadScript(Setup.Setup._setup.DefaultScript);
        }

        public void DoStance(MotionStance stance)
        {
            if (ViewObject != null)
                ViewObject.DoStance(stance);
        }

        public void DoMotion(MotionCommand motionCommand)
        {
            if (ViewObject != null)
                ViewObject.DoMotion(motionCommand);
        }

        public void Update(Microsoft.Xna.Framework.GameTime time)
        {
            if (ViewObject != null)
                ViewObject.Update(time);

            if (Camera != null)
                Camera.Update(time);

            var keyboardState = Keyboard.GetState();

            if (keyboardState.IsKeyDown(Keys.OemPeriod) && !PrevKeyboardState.IsKeyDown(Keys.OemPeriod))
            {
                PolyIdx++;
                Console.WriteLine($"PolyIdx: {PolyIdx}");
            }
            if (keyboardState.IsKeyDown(Keys.OemComma) && !PrevKeyboardState.IsKeyDown(Keys.OemComma))
            {
                PolyIdx--;
                Console.WriteLine($"PolyIdx: {PolyIdx}");
            }

            if (keyboardState.IsKeyDown(Keys.OemQuestion) && !PrevKeyboardState.IsKeyDown(Keys.OemQuestion))
            {
                PartIdx++;
                Console.WriteLine($"PartIdx: {PartIdx}");
            }
            if (keyboardState.IsKeyDown(Keys.M) && !PrevKeyboardState.IsKeyDown(Keys.M))
            {
                PartIdx--;
                Console.WriteLine($"PartIdx: {PartIdx}");
            }
        }

        public void Draw(Microsoft.Xna.Framework.GameTime time)
        {
            Effect.CurrentTechnique = Effect.Techniques["TexturedNoShading"];
            Effect.Parameters["xWorld"].SetValue(Matrix.Identity);
            Effect.Parameters["xView"].SetValue(Camera.ViewMatrix);
            Effect.Parameters["xProjection"].SetValue(Camera.ProjectionMatrix);
            Effect.Parameters["xOpacity"].SetValue(1.0f);

            Effect_Clamp.CurrentTechnique = Effect_Clamp.Techniques["TexturedNoShading"];
            Effect_Clamp.Parameters["xWorld"].SetValue(Matrix.Identity);
            Effect_Clamp.Parameters["xView"].SetValue(Camera.ViewMatrix);
            Effect_Clamp.Parameters["xProjection"].SetValue(Camera.ProjectionMatrix);
            Effect_Clamp.Parameters["xOpacity"].SetValue(1.0f);

            switch (ModelType)
            {
                case ModelType.Setup:
                    DrawModel();
                    break;
                case ModelType.Environment:
                    DrawEnvironment();
                    break;
                case ModelType.EnvCell:
                    DrawEnvCell();
                    break;
            }

            if (MainMenu.ShowHUD)
                GameView.Instance.Render.DrawHUD();
        }

        // for debugging
        public static int PolyIdx { get; set; } = -1;

        public static int PartIdx { get; set; } = -1;

        public void DrawModel()
        {
            if (Setup == null) return;

            GraphicsDevice.Clear(ConfigManager.Config.BackgroundColors.ModelViewer);

            EnsureGroundResources();

            // Reflection pass
            DrawReflection();

            // Ground plane
            DrawGroundPlane();

            // Main model
            Setup.Draw(PolyIdx, PartIdx);

            if (ViewObject.PhysicsObj.ParticleManager != null)
                ParticleViewer.Instance.DrawParticles(ViewObject.PhysicsObj);
        }

        public void DrawEnvironment()
        {
            Effect.CurrentTechnique = Effect.Techniques["ColoredNoShading"];
            GraphicsDevice.Clear(new Color(48, 48, 48));

            if (Environment != null)
                Environment.Draw();
        }

        public void DrawEnvCell()
        {
            GraphicsDevice.Clear(ConfigManager.Config.BackgroundColors.ModelViewer);

            if (EnvCell != null)
                EnvCell.Draw(Matrix.Identity);
        }
    }
}

