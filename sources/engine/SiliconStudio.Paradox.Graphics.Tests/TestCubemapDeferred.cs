﻿// Copyright (c) 2014 Silicon Studio Corp. (http://siliconstudio.co.jp)
// This file is distributed under GPL v3. See LICENSE.md for details.

using System;
using System.Threading.Tasks;

using NUnit.Framework;

using SiliconStudio.Core.Mathematics;
using SiliconStudio.Paradox.Effects;
using SiliconStudio.Paradox.Effects.Cubemap;
using SiliconStudio.Paradox.Effects.Renderers;
using SiliconStudio.Paradox.Engine;
using SiliconStudio.Paradox.EntityModel;
using SiliconStudio.Paradox.Extensions;

namespace SiliconStudio.Paradox.Graphics.Tests
{
    public class TestCubemapDeferred : TestGameBase
    {
        private LightingIBLRenderer IBLRenderer;

        private Entity teapotEntity;

        private Entity dynamicCubemapEntity;

        public TestCubemapDeferred()
        {
            GraphicsDeviceManager.PreferredGraphicsProfile = new[] { GraphicsProfile.Level_11_0 };
        }

        protected override async Task LoadContent()
        {
            await base.LoadContent();

            // create pipeline
            CreatePipeline();

            // setup the scene
            var material = Asset.Load<Material>("BasicMaterial");
            teapotEntity = new Entity()
            {
                new ModelComponent()
                {
                    Model = new Model()
                    {
                        new Mesh()
                        {
                            Draw = GeometricPrimitive.Teapot.New(GraphicsDevice).ToMeshDraw(),
                            Material = material
                        }
                    }
                }
            };
            Entities.Add(teapotEntity);

            var textureCube = Asset.Load<Texture>("uv_cube");
            var staticCubemapEntity = new Entity()
            {
                new CubemapSourceComponent(textureCube) { Enabled = true, InfluenceRadius = 2f, IsDynamic = false },
                new TransformationComponent() { Translation = Vector3.UnitZ }
            };
            Entities.Add(staticCubemapEntity);

            dynamicCubemapEntity = new Entity()
            {
                new CubemapSourceComponent(textureCube) { Enabled = true, InfluenceRadius = 0.5f, IsDynamic = false },
                new TransformationComponent() { Translation = Vector3.Zero }
            };
            Entities.Add(dynamicCubemapEntity);

            var mainCamera = new Entity()
            {
                new CameraComponent
                {
                    AspectRatio = 8/4.8f,
                    FarPlane = 20,
                    NearPlane = 1,
                    VerticalFieldOfView = 0.6f,
                    Target = teapotEntity,
                    TargetUp = Vector3.UnitY,
                },
                new TransformationComponent
                {
                    Translation = new Vector3(4, 3, 0)
                }
            };
            Entities.Add(mainCamera);

            RenderSystem.Pipeline.SetCamera(mainCamera.Get<CameraComponent>());

            Script.Add(GameScript1);
        }

        private void CreatePipeline()
        {
            // Processor
            Entities.Processors.Add(new CubemapSourceProcessor(GraphicsDevice));

            // Rendering pipeline
            RenderSystem.Pipeline.Renderers.Add(new CameraSetter(Services));

            RenderSystem.Pipeline.Renderers.Add(new RenderTargetSetter(Services)
            {
                ClearColor = Color.CornflowerBlue,
                EnableClearDepth = true,
                ClearDepth = 1f
            });

            // Create G-buffer pass
            var gbufferPipeline = new RenderPipeline("GBuffer");
            // Renders the G-buffer for opaque geometry.
            gbufferPipeline.Renderers.Add(new ModelRenderer(Services, "CubemapIBLEffect.ParadoxGBufferShaderPass"));
            var gbufferProcessor = new GBufferRenderProcessor(Services, gbufferPipeline, GraphicsDevice.DepthStencilBuffer, false);

            // Add sthe G-buffer pass to the pipeline.
            RenderSystem.Pipeline.Renderers.Add(gbufferProcessor);

            var readOnlyDepthBuffer = GraphicsDevice.DepthStencilBuffer; // TODO ToDepthStencilBuffer(true);
            IBLRenderer = new LightingIBLRenderer(Services, "CubemapIBLSpecular", readOnlyDepthBuffer);
            RenderSystem.Pipeline.Renderers.Add(IBLRenderer);
            RenderSystem.Pipeline.Renderers.Add(new RenderTargetSetter(Services)
            {
                ClearColor = Color.CornflowerBlue,
                EnableClearDepth = false,
            });
            RenderSystem.Pipeline.Renderers.Add(new DelegateRenderer(Services) { Render = ShowIBL });
        }

        private void ShowIBL(RenderContext context)
        {
            GraphicsDevice.DrawTexture(IBLRenderer.IBLTexture);
        }

        private async Task GameScript1()
        {
            while (IsRunning)
            {
                // Wait next rendering frame
                await Script.NextFrame();

                teapotEntity.Transformation.Rotation = Quaternion.RotationY((float)(2 * Math.PI * UpdateTime.Total.TotalMilliseconds / 5000.0f));
                dynamicCubemapEntity.Transformation.Translation = new Vector3(2f * (float)Math.Sin(2 * Math.PI * UpdateTime.Total.TotalMilliseconds / 15000.0f), 0, 0);
            }
        }

        public static void Main()
        {
            using (var game = new TestCubemapDeferred())
                game.Run();
        }

        /// <summary>
        /// Run the test
        /// </summary>
        [Test]
        public void RunCubemapRendering()
        {
            RunGameTest(new TestCubemapDeferred());
        }
    }
}