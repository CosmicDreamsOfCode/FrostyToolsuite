using Frosty.Core;
using FrostySdk;
using FrostySdk.Interfaces;
using FrostySdk.IO;
using FrostySdk.Managers;
using MeshSetPlugin.Resources;
using SharpDX;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MeshSetPlugin.Fbx
{

    #region -- Importer Exceptions --
    public class FBXImportInvalidLodCountException : Exception
    {
        public FBXImportInvalidLodCountException(int foundCount, int expectedCount)
            : base(string.Format("There was a mismatch in the amount of LODs defined in the imported file and existing mesh. Found {0}, Expected {1}. Make sure you follow the format of [section]:lod[index]", foundCount, expectedCount))
        {
        }
    }
    public class FBXImportInvalidSectionCountException : Exception
    {
        public FBXImportInvalidSectionCountException(int lodLevel)
            : base(string.Format("There was a mismatch in the amount of sections defined in the imported file for lod level {0} and existing mesh", lodLevel))
        {
        }
    }
    public class FBXImportInvalidMeshTypeException : Exception
    {
        public FBXImportInvalidMeshTypeException()
            : base(string.Format("Only rigid and skinned meshes are supported at this time"))
        {
        }
    }
    public class FBXImportMissingUvsException : Exception
    {
        public FBXImportMissingUvsException()
            : base(string.Format("Mesh must be exported with at least one UV channel"))
        {
        }
    }
    public class FBXImportMissingTangentsException : Exception
    {
        public FBXImportMissingTangentsException()
            : base(string.Format("Mesh must be exported with tangents/binormals"))
        {
        }
    }
    public class FBXImportUnimplementDataTypeException : Exception
    {
        public FBXImportUnimplementDataTypeException(VertexElementUsage usage)
            : base(string.Format("Mesh contains an unimplemented vertex data type '" + usage.ToString() + "'"))
        {
        }
    }
    public class FBXImportNoMeshesFoundException : Exception
    {
        public FBXImportNoMeshesFoundException(int lodLevel)
            : base(string.Format("Import file must contain at least one mesh at lod level " + lodLevel))
        {
        }
    }
    public class FBXImportMissingWeightException : Exception
    {
        public FBXImportMissingWeightException(string sectionName)
            : base(string.Format("Encountered face on imported object {0} without any bone assigned. Make sure your model is fully skinned", sectionName))
        {
        }
    }

    public class FBXImportNotTriangulatedException : Exception
    {
        public FBXImportNotTriangulatedException(string sectionName)
            : base(string.Format("Encountered face that wasn't triangulated on imported object {0}. Make sure to triangulate your mesh before importing!", sectionName))
        {
        }
    }
    #endregion

    public class FBXImporter
    {
        public float Scale { get; set; } = 1.0f;
        public int XAxis { get; set; } = 0;
        public int YAxis { get; set; } = 1;
        public int ZAxis { get; set; } = 2;
        public float FlipZ { get; set; } = 1.0f;

        private MeshSet meshSet;
        private List<ShaderBlockDepot> shaderBlockDepots;
        private ResAssetEntry resEntry;
        private ILogger logger;
        private MeshImportSettings settings;
        private BoundingBox boundingBox;
        private BoundingBox previousBbox;

        public FBXImporter(ILogger inLogger)
        {
            logger = inLogger;
        }

        public void ImportFBX(string filename, MeshSet inMeshSet, EbxAsset asset, EbxAssetEntry entry, MeshImportSettings inSettings)
        {
            ulong resRid = ((dynamic)asset.RootObject).MeshSetResource;
            resEntry = App.AssetManager.GetResEntry(resRid);

            settings = inSettings;
            meshSet = inMeshSet;

            shaderBlockDepots = new List<ShaderBlockDepot>();
            if (ProfilesLibrary.DataVersion == (int)ProfileVersion.StarWarsBattlefrontII)
            {
                // collect every shader block depot that is used by this mesh
                string path = "/" + entry.Filename.ToLower();
                foreach (ResAssetEntry sbeEntry in App.AssetManager.EnumerateRes(resType: (uint)ResourceType.ShaderBlockDepot))
                {
                    if (sbeEntry.Name.Contains(path))
                    {
                        shaderBlockDepots.Add(App.AssetManager.GetResAs<ShaderBlockDepot>(sbeEntry));
                    }
                }
            }

            // @hack
            entry.LinkedAssets.Clear();
            resEntry.LinkedAssets.Clear();

            using (FbxManager manager = new FbxManager())
            {
                FbxIOSettings fbxSettings = new FbxIOSettings(manager, FbxIOSettings.IOSROOT);
                manager.SetIOSettings(fbxSettings);

                FbxScene scene = new FbxScene(manager, "");
                LoadScene(manager, scene, filename);

                List<FbxNode>[] lodNodes = new List<FbxNode>[7];
                int lodCount = 0;

                // look nodes
                foreach (FbxNode child in scene.RootNode.Children)
                {
                    string nodeName = child.Name.ToLower();
                    if (nodeName.Contains("lod"))
                    {
                        if (nodeName.Contains(":"))
                        {
                            // flat hierarchy, contains section:lodX
                            nodeName = nodeName.Substring(nodeName.LastIndexOf(':') + 4);

                            if (int.TryParse(nodeName, out int lodIndex))
                            {
                                if (lodNodes[lodIndex] == null)
                                {
                                    lodNodes[lodIndex] = new List<FbxNode>();
                                    lodCount++;
                                }
                                lodNodes[lodIndex].Add(child);
                            }
                        }
                        else
                        {
                            // standard hierarchy
                            nodeName = nodeName.Substring(nodeName.Length - 1);

                            if (int.TryParse(nodeName, out int lodIndex))
                            {
                                if (lodNodes[lodIndex] == null)
                                {
                                    lodNodes[lodIndex] = new List<FbxNode>();
                                    lodCount++;
                                }
                                lodNodes[lodIndex].AddRange(child.Children);
                            }
                        }
                    }
                }

                if (lodCount < meshSet.Lods.Count)
                    throw new FBXImportInvalidLodCountException(lodCount, meshSet.Lods.Count);

                meshSet.ClearPartData();
                List<BoundingBox> partBbox = new List<BoundingBox>();
                List<LinearTransform> transforms = new List<LinearTransform>();
                AxisAlignedBox AABBToWrite = new AxisAlignedBox();
                // process each lod
                for (int i = 0; i < meshSet.Lods.Count; i++)
                {
                    ProcessLod(lodNodes[i], i, ref partBbox, ref transforms);
                }

                if (meshSet.Type == MeshType.MeshType_Composite)
                {
                    meshSet.SetParts(ToAxisAlignedBoundingBoxes(partBbox), transforms);
                }

                AABBToWrite = ToAxisAlignedBoundingBox(boundingBox);
                meshSet.BoundingBox = AABBToWrite;
            }

            meshSet.FullName = resEntry.Name;

            // modify resource
            App.AssetManager.ModifyRes(resRid, meshSet);
            entry.LinkAsset(resEntry);
        }

        private List<AxisAlignedBox> ToAxisAlignedBoundingBoxes(List<BoundingBox> inBoundingBoxes)
        {
            List<AxisAlignedBox> retVal = new List<AxisAlignedBox>(inBoundingBoxes.Count);

            foreach (BoundingBox bbox in inBoundingBoxes)
            {
                Vec3 min = new Vec3();
                min.x = bbox.Minimum.X;
                min.y = bbox.Minimum.Y;
                min.z = bbox.Minimum.Z;

                Vec3 max = new Vec3();
                max.x = bbox.Maximum.X;
                max.y = bbox.Maximum.Y;
                max.z = bbox.Maximum.Z;

                retVal.Add(new AxisAlignedBox() { min = min, max = max });
            }

            return retVal;
        }

        private AxisAlignedBox ToAxisAlignedBoundingBox(BoundingBox inBoundingBox)
        {
            AxisAlignedBox retVal = new AxisAlignedBox();

            Vec3 min = new Vec3();
            min.x = inBoundingBox.Minimum.X;
            min.y = inBoundingBox.Minimum.Y;
            min.z = inBoundingBox.Minimum.Z;

            Vec3 max = new Vec3();
            max.x = inBoundingBox.Maximum.X;
            max.y = inBoundingBox.Maximum.Y;
            max.z = inBoundingBox.Maximum.Z;

            retVal.min = min;
            retVal.max = max;

            return retVal;
        }

        private float CubeMapFaceID(float inX, float inY, float inZ)
        {
            if (Math.Abs(inZ) >= Math.Abs(inX) && Math.Abs(inZ) >= Math.Abs(inY))
                return (inZ < 0.0f) ? 5.0f : 4.0f; // faceID

            if (Math.Abs(inY) >= Math.Abs(inX))
                return (inY < 0.0f) ? 3.0f : 2.0f; // faceID

            return (inX < 0.0f) ? 1.0f : 0.0f; // faceID
        }

        private int FindGreatestComponent(Quaternion quat)
        {
            int xyzIndex = (int)(CubeMapFaceID(quat.X, quat.Y, quat.Z) * 0.5f);
            const int wIndex = 3;

            bool wBiggest = Math.Abs(quat.W) > Math.Max(Math.Abs(quat.X), Math.Max(Math.Abs(quat.Y), Math.Abs(quat.Z)));
            return (wBiggest) ? wIndex : xyzIndex;
        }

        private void LoadScene(FbxManager manager, FbxScene scene, string filename)
        {
            FbxManager.GetFileFormatVersion(out int major, out int minor, out int revision);

            using (FbxImporter importer = new FbxImporter(manager, ""))
            {
                if (!importer.Initialize(filename, -1, manager.GetIOSettings()))
                {
                    // unable to continue, most likely invalid file version
                    string errorString = importer.Status.ErrorString;
                    logger.Log(errorString);
                    return;
                }

                importer.GetFileVersion(out int fileMajor, out int fileMinor, out int fileRevision);

                if (importer.IsFBX())
                {
                    // try to import
                    bool status = importer.Import(scene);
                    if (!status)
                    {
                        // failed
                        logger.Log(importer.Status.ErrorString);
                        return;
                    }
                }
            }

            FbxDocumentInfo info = scene.SceneInfo;
        }

        private void ProcessLod(List<FbxNode> nodes, int lodIndex, ref List<BoundingBox> partBbox, ref List<LinearTransform> transforms)
        {
            MeshSetLod meshLod = meshSet.Lods[lodIndex];
            List<FbxNode> sectionNodes = new List<FbxNode>();

            foreach (FbxNode child in nodes)
            {
                FbxNodeAttribute attr = child.GetNodeAttribute(FbxNodeAttribute.EType.eMesh);
                if (attr != null)
                    sectionNodes.Add(child);
            }

            if (sectionNodes.Count == 0)
                throw new FBXImportNoMeshesFoundException(lodIndex);

            List<MeshSetSection> meshSections = new List<MeshSetSection>();
            List<MeshSetSection> depthSections = new List<MeshSetSection>();
            List<MeshSetSection> shadowSections = new List<MeshSetSection>();

            foreach (MeshSetSection meshSection in meshLod.Sections)
            {
                if (meshSection.Name != "")
                    meshSections.Add(meshSection);
                else
                {
                    if (meshLod.IsSectionInCategory(meshSection, MeshSubsetCategory.MeshSubsetCategory_ZOnly))
                        depthSections.Add(meshSection);
                    else
                        shadowSections.Add(meshSection);
                }
            }

            List<FbxNode> sectionNodeMapping = new List<FbxNode>();
            List<FbxNode> unclaimedNodes = new List<FbxNode>();
            List<FbxNode> allNodes = new List<FbxNode>();
            sectionNodeMapping.AddRange(new FbxNode[meshSections.Count]);

            foreach (var node in sectionNodes)
            {
                string sectionName = node.Name;
                if (sectionName.Contains(':'))
                {
                    // remove the lod portion of the name
                    sectionName = sectionName.Remove(sectionName.IndexOf(':'));
                }

                int idx = meshSections.FindIndex((a) => a.Name == sectionName);
                if (idx != -1 && sectionNodeMapping[idx] == null)
                    sectionNodeMapping[idx] = node;
                else
                    unclaimedNodes.Add(node);
            }

            List<byte[]> sectionsVertices = new List<byte[]>();
            List<List<uint>> sectionsIndices = new List<List<uint>>();
            uint vertexBufferSize = 0;
            uint totalIndices = 0;

            // clear out old bones
            meshLod.ClearBones();

            // process each mesh renderable section first
            for (int i = 0; i < sectionNodeMapping.Count; i++)
            {
                var node = sectionNodeMapping[i];
                int sectionIndex = meshLod.Sections.IndexOf(meshSections[i]);

                if (node == null)
                {
                    if (unclaimedNodes.Count > 0)
                    {
                        node = unclaimedNodes.First();
                        unclaimedNodes.RemoveAt(0);
                    }
                    else
                    {
                        // kill section
                        meshSections[i].VertexCount = 0;
                        meshSections[i].PrimitiveCount = 0;
                        meshSections[i].VertexOffset = 0;
                        meshSections[i].StartIndex = 0;
                        continue;
                    }
                }

                MemoryStream vertices = new MemoryStream();
                List<uint> indices = new List<uint>();

                ProcessSection(new FbxNode[] { node }, meshLod, sectionIndex, vertices, indices, vertexBufferSize, ref totalIndices);
                boundingBox = BoundingBox.Merge(previousBbox, boundingBox);
                previousBbox = boundingBox;


                sectionsVertices.Add(vertices.ToArray());
                sectionsIndices.Add(indices);

                vertexBufferSize += (uint)vertices.Length;
                vertices.Dispose();

                allNodes.Add(node);
            }

            // depth sections
            for (int i = 0; i < depthSections.Count; i++)
            {
                int sectionIndex = meshLod.Sections.IndexOf(depthSections[i]);

                if (i == 0)
                {
                    MemoryStream vertices = new MemoryStream();
                    List<uint> indices = new List<uint>();

                    ProcessSection(allNodes.ToArray(), meshLod, sectionIndex, vertices, indices, vertexBufferSize, ref totalIndices);
                    boundingBox = BoundingBox.Merge(previousBbox, boundingBox);
                    previousBbox = boundingBox;

                    sectionsVertices.Add(vertices.ToArray());
                    sectionsIndices.Add(indices);

                    vertexBufferSize += (uint)vertices.Length;
                    vertices.Dispose();
                }
                else
                {
                    // kill section
                    depthSections[i].VertexCount = 0;
                    depthSections[i].PrimitiveCount = 0;
                    depthSections[i].VertexOffset = 0;
                    depthSections[i].StartIndex = 0;
                }
            }

            // shadow sections
            for (int i = 0; i < shadowSections.Count; i++)
            {
                int sectionIndex = meshLod.Sections.IndexOf(shadowSections[i]);

                if (i == 0)
                {
                    MemoryStream vertices = new MemoryStream();
                    List<uint> indices = new List<uint>();

                    ProcessSection(allNodes.ToArray(), meshLod, sectionIndex, vertices, indices, vertexBufferSize, ref totalIndices);
                    boundingBox = BoundingBox.Merge(previousBbox, boundingBox);
                    previousBbox = boundingBox;

                    sectionsVertices.Add(vertices.ToArray());
                    sectionsIndices.Add(indices);

                    vertexBufferSize += (uint)vertices.Length;
                    vertices.Dispose();
                }
                else
                {
                    // kill section
                    shadowSections[i].VertexCount = 0;
                    shadowSections[i].PrimitiveCount = 0;
                    shadowSections[i].VertexOffset = 0;
                    shadowSections[i].StartIndex = 0;
                }
            }

            if (meshSet.Type == MeshType.MeshType_Composite)
            {
                CalculateCompositePartDataForLod(sectionNodeMapping, meshLod, ref partBbox, ref transforms);
            }

            if (ProfilesLibrary.DataVersion == (int)ProfileVersion.StarWarsBattlefrontII)
            {
                // update shader block depot mesh parameters
                foreach (var depot in shaderBlockDepots)
                {
                    var sbe = depot.GetSectionEntry(lodIndex);
                    for (int i = 0; i < meshLod.Sections.Count; i++)
                    {
                        MeshParamDbBlock meshParams = sbe.GetMeshParams(i);
                        if (meshParams != null)
                        {
                            meshParams.SetParameterValue("!primitiveCount", meshLod.Sections[i].PrimitiveCount);
                            meshParams.SetParameterValue("!vertexStreamOffsets0", meshLod.Sections[i].VertexOffset);
                            meshParams.SetParameterValue("!startIndex", meshLod.Sections[i].StartIndex);
                            meshParams.IsModified = true;
                        }
                    }

                    App.AssetManager.ModifyRes(depot.ResourceId, depot);
                    resEntry.LinkAsset(App.AssetManager.GetResEntry(depot.ResourceId));
                }
            }

            bool largeIndexBuffer = false;
            foreach (var section in meshLod.Sections)
            {
                if (section.VertexCount > 65535)
                {
                    largeIndexBuffer = true;
                    break;
                }
            }

            // write out new chunk/inline data
            using (NativeWriter writer = new NativeWriter(new MemoryStream()))
            {
                foreach (byte[] buffer in sectionsVertices)
                    writer.Write(buffer);
                writer.WritePadding(0x10);
                meshLod.VertexBufferSize = (uint)writer.Position;
                foreach (List<uint> buffer in sectionsIndices)
                {
                    foreach (uint index in buffer)
                    {
                        if (largeIndexBuffer)
                            writer.Write(index);
                        else
                            writer.Write((ushort)index);
                    }
                }
                writer.WritePadding(0x10);
                meshLod.IndexBufferSize = (uint)(writer.Position - meshLod.VertexBufferSize);
                meshLod.SetIndexBufferFormatSize((largeIndexBuffer) ? 4 : 2);

                if (meshLod.ChunkId != Guid.Empty)
                {
                    // modify the chunk
                    App.AssetManager.ModifyChunk(meshLod.ChunkId, writer.ToByteArray());

                    ChunkAssetEntry chunkEntry = App.AssetManager.GetChunkEntry(meshLod.ChunkId);
                    resEntry.LinkAsset(chunkEntry);
                }
                else
                {
                    // modify inline data
                    meshLod.SetInlineData(writer.ToByteArray());
                }
            }
        }

        private LinearTransform FbxTransformToLinearTransform(FbxNode inFbxNode)
        {
            LinearTransform trns = new LinearTransform();
            float pi = (float)(Math.PI / 180.0);

            // Setup the rotation matrix
            Vector3 fbxRotation = new Vector3(inFbxNode.LclRotation.X * pi, inFbxNode.LclRotation.Y * pi, inFbxNode.LclRotation.Z * pi);

            Matrix rotMatrix = Matrix.Identity;

            rotMatrix *= Matrix.RotationX(fbxRotation.X);
            rotMatrix *= Matrix.RotationY(fbxRotation.Y);
            rotMatrix *= Matrix.RotationZ(fbxRotation.Z);

            // Now create the LinearTransform matrix
            Vector3 nodeScale = inFbxNode.LclScaling;
            Matrix fbMatrix = new Matrix();
            fbMatrix.M11 = rotMatrix.M11 * nodeScale.X;
            fbMatrix.M12 = rotMatrix.M12 * nodeScale.Y;
            fbMatrix.M13 = rotMatrix.M13 * nodeScale.Z;

            fbMatrix.M21 = rotMatrix.M21 * nodeScale.X;
            fbMatrix.M22 = rotMatrix.M22 * nodeScale.Y;
            fbMatrix.M23 = rotMatrix.M23 * nodeScale.Z;

            fbMatrix.M31 = rotMatrix.M31 * nodeScale.X;
            fbMatrix.M32 = rotMatrix.M32 * nodeScale.Y;
            fbMatrix.M33 = rotMatrix.M33 * nodeScale.Z;

            fbMatrix.M41 = inFbxNode.LclTranslation.X;
            fbMatrix.M42 = inFbxNode.LclTranslation.Y;
            fbMatrix.M43 = inFbxNode.LclTranslation.Z;

            fbMatrix.M14 = 0f;
            fbMatrix.M24 = 0f;
            fbMatrix.M34 = 0f;
            fbMatrix.M44 = 1f;

            // Converting from the SharpDX Matrix
            trns.trans.x = inFbxNode.LclTranslation.X;
            trns.trans.y = inFbxNode.LclTranslation.Y;
            trns.trans.z = inFbxNode.LclTranslation.Z;

            trns.right.x = fbMatrix.M11;
            trns.right.y = fbMatrix.M12;
            trns.right.z = fbMatrix.M13;

            trns.up.x = fbMatrix.M21;
            trns.up.y = fbMatrix.M22;
            trns.up.z = fbMatrix.M23;

            trns.forward.x = fbMatrix.M31;
            trns.forward.y = fbMatrix.M32;
            trns.forward.z = fbMatrix.M33;

            return trns;
        }

        private List<Vector3> GetVerticesFromCluster(FbxMesh fmesh, FbxCluster cluster, Matrix sectionMatrix)
        {
            IntPtr verticesBuffer = fmesh.GetControlPoints();

            int[] controlPointIndices = cluster.GetControlPointIndices();
            int controlPointIndiceCount = controlPointIndices.Count();
            List<int> vertexIndices = new List<int>();
            List<Vector3> vertexPositions = new List<Vector3>();

            for (int i = 0; i < fmesh.PolygonCount; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    int vertexIndex = fmesh.GetPolygonIndex(i, j);

                    if (controlPointIndiceCount > vertexIndex && controlPointIndices[vertexIndex] >= 0)
                    {
                        vertexIndices.Add(vertexIndex);
                    }
                }
            }

            foreach (int vIdx in vertexIndices)
            {
                Vector3 position;
                unsafe
                {
                    double* pointsPtr = (double*)(verticesBuffer + (vIdx * 32));
                    position = new Vector3((float)pointsPtr[XAxis] * Scale, (float)pointsPtr[YAxis] * Scale, (float)(pointsPtr[ZAxis] * FlipZ) * Scale);
                }

                Vector4 tmp = Vector3.Transform(position, sectionMatrix);
                position.X = tmp.X;
                position.Y = tmp.Y;
                position.Z = tmp.Z;

                vertexPositions.Add(position);
            }

            return vertexPositions;
        }

        private void CalculateCompositePartDataForLod(List<FbxNode> nodes, MeshSetLod meshLod, ref List<BoundingBox> bbox, ref List<LinearTransform> transforms)
        {
            List<LinearTransform> partTransforms = new List<LinearTransform>();
            List<AxisAlignedBox> partAABBs = new List<AxisAlignedBox>();
            List<List<int>> partIndices = new List<List<int>>();
            Dictionary<ushort, List<Vector3>> boneToVerticesMapping = new Dictionary<ushort, List<Vector3>>();

            foreach (FbxNode node in nodes)
            {
                FbxNodeAttribute attr = node.GetNodeAttribute(FbxNodeAttribute.EType.eMesh);
                FbxMesh fmesh = new FbxMesh(attr);
                FbxSkin fskin = (fmesh.GetDeformerCount(FbxDeformer.EDeformerType.eSkin) != 0)
                    ? new FbxSkin(fmesh.GetDeformer(0, FbxDeformer.EDeformerType.eSkin))
                    : null;

                if (fskin == null)
                    return;
                List<int> boneList = new List<int>();

                foreach (FbxCluster cluster in fskin.Clusters)
                {
                    if (cluster.ControlPointIndicesCount == 0)
                        continue;

                    FbxNode bone = cluster.GetLink();
                    ushort idx = ushort.Parse(bone.Name.Substring(bone.Name.LastIndexOf('_') + 1));
                    boneList.Add(idx);

                    Matrix sectionMatrix = new FbxMatrix(node.EvaluateGlobalTransform()).ToSharpDX();

                    if (!boneToVerticesMapping.ContainsKey(idx))
                    {
                        while (partTransforms.Count <= idx)
                        {
                            partTransforms.Add(LinearTransform.Identity);
                        }
                        partTransforms[idx] = FbxTransformToLinearTransform(bone);
                        while (transforms.Count <= idx)
                        {
                            transforms.Add(LinearTransform.Identity);
                        }
                        transforms[idx] = partTransforms[idx];

                        boneToVerticesMapping.Add(idx, GetVerticesFromCluster(fmesh, cluster, sectionMatrix));
                    }
                    else
                    {
                        boneToVerticesMapping[idx].AddRange(GetVerticesFromCluster(fmesh, cluster, sectionMatrix));
                    }
                }

                partIndices.Add(boneList);
            }

            foreach (KeyValuePair<ushort, List<Vector3>> boneAndVertices in boneToVerticesMapping)
            {
                (AxisAlignedBox, BoundingBox) aabbs = AABBFromPoints(boneAndVertices.Value);
                while (partAABBs.Count <= boneAndVertices.Key)
                {
                    partAABBs.Add(default);
                }
                partAABBs[boneAndVertices.Key] = aabbs.Item1;
                if (bbox.Count > boneAndVertices.Key)
                {
                    bbox[boneAndVertices.Key] = BoundingBox.Merge(bbox[boneAndVertices.Key], aabbs.Item2);
                }
                else
                {
                    for (int i = bbox.Count; i < boneAndVertices.Key; i++)
                    {
                        bbox.Add(new BoundingBox());
                    }
                    bbox.Add(aabbs.Item2);
                }
            }

            meshLod.ClearPartData();
            meshLod.SetParts(partTransforms, partAABBs, partIndices);
        }

        private void ProcessSection(FbxNode[] sectionNodes, MeshSetLod meshLod, int sectionIndex, MemoryStream verticesBuffer, List<uint> indicesBuffer, uint vertexOffset, ref uint startIndex)
        {
            MeshSetSection meshSection = meshLod.Sections[sectionIndex];
            uint indexOffset = 0;

            meshSection.VertexOffset = vertexOffset;
            meshSection.StartIndex = startIndex;

            meshSection.VertexCount = 0;
            meshSection.PrimitiveCount = 0;

            List<ushort> boneList = new List<ushort>();
            List<string> boneNames = new List<string>();
            List<string> procBones = new List<string>();

            // load in skeleton
            dynamic skeleton = null;
            if (settings != null && settings.SkeletonAsset != "")
                skeleton = App.AssetManager.GetEbx(App.AssetManager.GetEbxEntry(settings.SkeletonAsset)).RootObject;

            // collect bones first
            foreach (FbxNode sectionNode in sectionNodes)
            {
                if (sectionNode == null)
                    continue;

                FbxNodeAttribute attr = sectionNode.GetNodeAttribute(FbxNodeAttribute.EType.eMesh);
                FbxMesh fmesh = new FbxMesh(attr);
                FbxSkin fskin = (fmesh.GetDeformerCount(FbxDeformer.EDeformerType.eSkin) != 0)
                    ? new FbxSkin(fmesh.GetDeformer(0, FbxDeformer.EDeformerType.eSkin))
                    : null;

                if (fskin != null)
                {
                    // collect any procedural bones
                    foreach (FbxCluster cluster in fskin.Clusters)
                    {
                        FbxNode bone = cluster.GetLink();
                        if (bone.Name.StartsWith("PROC"))
                        {
                            if (!procBones.Contains(bone.Name))
                                procBones.Add(bone.Name);
                        }
                    }

                    // Madden19/FIFA17/FIFA18/Madden20
                    if (meshSet.Type != MeshType.MeshType_Composite && (ProfilesLibrary.DataVersion == (int)ProfileVersion.Madden19 || ProfilesLibrary.DataVersion == (int)ProfileVersion.Fifa17 || ProfilesLibrary.DataVersion == (int)ProfileVersion.Fifa18 || ProfilesLibrary.DataVersion == (int)ProfileVersion.Madden20))
                    {
                        // @todo: if works, then merge with below
                        for (int i = 0; i < skeleton.BoneNames.Count; i++)
                        {
                            if (!boneList.Contains((ushort)i))
                            {
                                boneList.Add((ushort)i);
                                boneNames.Add(skeleton.BoneNames[i]);
                            }
                        }
                    }

                    // MEC/BF1/SWBF2/BFV/Anthem/FIFA19/FIFA20/BFN/SWS
                    else if (meshSet.Type != MeshType.MeshType_Composite && (ProfilesLibrary.DataVersion == (int)ProfileVersion.MirrorsEdgeCatalyst ||
                                                                             ProfilesLibrary.DataVersion == (int)ProfileVersion.Battlefield5 ||
                                                                             ProfilesLibrary.DataVersion == (int)ProfileVersion.StarWarsBattlefrontII ||
                                                                             ProfilesLibrary.DataVersion == (int)ProfileVersion.Fifa19 ||
                                                                             ProfilesLibrary.DataVersion == (int)ProfileVersion.Fifa20 ||
                                                                             ProfilesLibrary.DataVersion == (int)ProfileVersion.StarWarsSquadrons))
                    {
                        // ushort/uint, can handle long lists so just put all bones into sections
                        for (int i = 0; i < skeleton.BoneNames.Count; i++)
                        {
                            if (!boneList.Contains((ushort)i))
                            {
                                boneList.Add((ushort)i);
                                boneNames.Add(skeleton.BoneNames[i]);
                            }
                        }
                        foreach (string bone in procBones)
                        {
                            ushort boneIdx = ushort.Parse(bone.Replace("PROC_Bone", ""));
                            for (int i = 0; i <= boneIdx; i++)
                            {
                                if (!boneList.Contains((ushort)(skeleton.BoneNames.Count + i)))
                                {
                                    boneList.Add((ushort)(skeleton.BoneNames.Count + i));
                                    boneNames.Add("PROC_Bone" + i);
                                }
                            }
                        }
                    }
                    else
                    {
                        // byte, can only handle sublists, so only obtain bones used
                        foreach (FbxCluster cluster in fskin.Clusters)
                        {
                            if (cluster.ControlPointIndicesCount == 0)
                                continue;

                            FbxNode bone = cluster.GetLink();
                            ushort idx;
                            if (meshSet.Type == MeshType.MeshType_Composite)
                            {
                                // get id from bone name, composite bone names should always be in the format PART_X
                                idx = ushort.Parse(bone.Name.Substring(bone.Name.LastIndexOf('_') + 1));
                            }
                            else
                            {
                                idx = (ushort)skeleton.BoneNames.IndexOf(bone.Name);
                            }

                            if (!boneList.Contains(idx))
                            {
                                boneList.Add(idx);
                                if (meshSet.Type == MeshType.MeshType_Composite)
                                {
                                    boneNames.Add(bone.Name);
                                }
                                else
                                {
                                    boneNames.Add(skeleton.BoneNames[idx]);
                                }
                            }
                        }
                    }
                }
            }

            ushort[] boneListArray = boneList.ToArray();
            string[] boneNamesArray = boneNames.ToArray();
            Array.Sort(boneListArray, boneNamesArray);

            meshSection.SetBones(boneListArray);
            meshLod.AddBones(boneListArray, boneNamesArray);

            boneList.Clear();
            boneList.AddRange(boneListArray);

            foreach (FbxNode sectionNode in sectionNodes)
            {
                if (sectionNode == null)
                    continue;

                // obtain the mesh component of the node
                FbxNodeAttribute attr = sectionNode.GetNodeAttribute(FbxNodeAttribute.EType.eMesh);
                FbxMesh fmesh = new FbxMesh(attr);
                FbxSkin fskin = (fmesh.GetDeformerCount(FbxDeformer.EDeformerType.eSkin) != 0)
                    ? new FbxSkin(fmesh.GetDeformer(0, FbxDeformer.EDeformerType.eSkin))
                    : null;

                // check for mandatory UVs/tangents/binormals
                if (fmesh.GetElementUV(0, FbxLayerElement.EType.eUnknown) == null)
                    throw new FBXImportMissingUvsException();
                if (fmesh.GetElementTangent(0) == null || fmesh.GetElementBinormal(0) == null)
                    throw new FBXImportMissingTangentsException();

                Matrix sectionMatrix = new FbxMatrix(sectionNode.EvaluateGlobalTransform()).ToSharpDX();

                List<List<ushort>> boneIndices = new List<List<ushort>>();
                List<List<byte>> boneWeights = new List<List<byte>>();
                int totalBoneInfluences = 0;

                // grab all the indices/weights from the skin clusters
                if (fskin != null)
                {
                    // collect all indices and weights
                    foreach (FbxCluster cluster in fskin.Clusters)
                    {
                        if (cluster.ControlPointIndicesCount == 0)
                            continue;

                        int[] tmpIndices = cluster.GetControlPointIndices();
                        double[] tmpWeights = cluster.GetControlPointWeights();

                        FbxNode bone = cluster.GetLink();
                        ushort boneIdx = 0xFFFF;

                        if (procBones.Contains(bone.Name))
                        {
                            // append 0x8000 to proc bones
                            boneIdx = (ushort)(0x8000 | ushort.Parse(bone.Name.Replace("PROC_Bone", "")));
                        }
                        else
                        {
                            if (meshSet.Type == MeshType.MeshType_Composite)
                            {
                                boneIdx = ushort.Parse(bone.Name.Substring(bone.Name.LastIndexOf('_') + 1));
                            }
                            else
                            {
                                boneIdx = (ushort)skeleton.BoneNames.IndexOf(bone.Name);
                                boneIdx = (ushort)boneList.IndexOf(boneIdx);
                            }
                        }

                        for (int i = 0; i < tmpIndices.Length; i++)
                        {
                            int idx = tmpIndices[i];
                            while (boneIndices.Count <= idx)
                            {
                                boneIndices.Add(new List<ushort>());
                                boneWeights.Add(new List<byte>());
                            }

                            if (((byte)Math.Round(tmpWeights[i] * 255.0)) > 0)
                            {
                                boneIndices[idx].Add(boneIdx);
                                boneWeights[idx].Add((byte)Math.Round(tmpWeights[i] * 255.0));
                            }
                        }
                    }

                    totalBoneInfluences = meshSection.BonesPerVertex;
                }

                // Write vertices into a temp import buffer, checking to see if there is a matching vertex already
                // stored, if so, return existing index,otherwise add new vertex and return the new index, this should reduce the
                // redundant vertices, but also ensure that vertices are duplicated in places where normals or UVs may differ

                List<DbObject> vertices = new List<DbObject>();
                vertices.AddRange(new DbObject[fmesh.ControlPointsCount]);

                List<int>[] indexToVertexMap = new List<int>[fmesh.ControlPointsCount];
                List<int> indices = new List<int>();
                IntPtr buffer = fmesh.GetControlPoints();
                int foundBoneInfluences = 0;

                for (int i = 0; i < fmesh.PolygonCount; i++)
                {
                    //check if polygon is comprised of more than 3 vertices
                    if (fmesh.GetPolygonSize(i) != 3)
                    {
                        throw new FBXImportNotTriangulatedException(sectionNode.Name);
                    }
                    for (int j = 0; j < 3; j++)
                    {
                        //int index = (i * 3) + j;
                        int vertexIndex = fmesh.GetPolygonIndex(i, j);
                        DbObject vertex = DbObject.CreateObject();

                        Vector4 position;
                        Vector3 normal;
                        Vector3 tangent;
                        Vector3 binormal;
                        ushort[] finalBoneIndices = null;
                        byte[] finalBoneWeights = null;

                        // position
                        unsafe
                        {
                            double* pointsPtr = (double*)(buffer + (vertexIndex * 32));
                            position = new Vector4((float)pointsPtr[XAxis] * Scale, (float)pointsPtr[YAxis] * Scale, (float)(pointsPtr[ZAxis] * FlipZ) * Scale, 1.0f);
                        }

                        // normal
                        {
                            FbxLayerElementNormal layerElemNormal = fmesh.GetElementNormal(0);
                            int mappingIndex = (layerElemNormal.MappingMode == EMappingMode.eByControlPoint) ? vertexIndex : (i * 3) + j;
                            int actualIndex = mappingIndex;
                            if (layerElemNormal.ReferenceMode != EReferenceMode.eDirect)
                                layerElemNormal.IndexArray.GetAt(mappingIndex, out actualIndex);

                            layerElemNormal.DirectArray.GetAt(actualIndex, out Vector4 tmp);
                            normal = new Vector3(tmp[XAxis], tmp[YAxis], tmp[ZAxis] * FlipZ);
                        }

                        // tangent
                        {
                            FbxLayerElementTangent layerElemTangent = fmesh.GetElementTangent(0);
                            int mappingIndex = (layerElemTangent.MappingMode == EMappingMode.eByControlPoint) ? vertexIndex : (i * 3) + j;
                            int actualIndex = mappingIndex;
                            if (layerElemTangent.ReferenceMode != EReferenceMode.eDirect)
                                layerElemTangent.IndexArray.GetAt(mappingIndex, out actualIndex);

                            layerElemTangent.DirectArray.GetAt(actualIndex, out Vector4 tmp);
                            tangent = new Vector3(tmp[XAxis], tmp[YAxis], tmp[ZAxis] * FlipZ);
                        }

                        // binormal
                        {
                            FbxLayerElementBinormal layerElemBinormal = fmesh.GetElementBinormal(0);
                            int mappingIndex = (layerElemBinormal.MappingMode == EMappingMode.eByControlPoint) ? vertexIndex : (i * 3) + j;
                            int actualIndex = mappingIndex;
                            if (layerElemBinormal.ReferenceMode != EReferenceMode.eDirect)
                                layerElemBinormal.IndexArray.GetAt(mappingIndex, out actualIndex);

                            layerElemBinormal.DirectArray.GetAt(actualIndex, out Vector4 tmp);
                            binormal = new Vector3(tmp[XAxis], tmp[YAxis], tmp[ZAxis] * FlipZ);
                        }

                        // indices/weights
                        if (fskin != null)
                        {
                            List<ushort> localBoneIndices = new List<ushort>();
                            List<byte> localBoneWeights = new List<byte>();

                            // sort indices/weights by biggest influence
                            var boneIndicesAndWeights = boneIndices[vertexIndex].Select((boneIndex, z) => new { boneIndex, boneWeight = boneWeights[vertexIndex][z] });
                            boneIndicesAndWeights = boneIndicesAndWeights.OrderByDescending(a => a.boneWeight);

                            localBoneIndices.AddRange(boneIndicesAndWeights.Select(a => a.boneIndex));
                            localBoneWeights.AddRange(boneIndicesAndWeights.Select(a => a.boneWeight));

                            foundBoneInfluences = (localBoneIndices.Count > foundBoneInfluences) ? localBoneIndices.Count : foundBoneInfluences;
                            while (localBoneIndices.Count > totalBoneInfluences)
                            {
                                // remove the lowest influence bones
                                localBoneIndices.RemoveRange(totalBoneInfluences, localBoneIndices.Count - totalBoneInfluences);
                                localBoneWeights.RemoveRange(totalBoneInfluences, localBoneWeights.Count - totalBoneInfluences);
                            }

                            if (localBoneIndices.Count == 0)
                                throw new FBXImportMissingWeightException(sectionNode.Name);

                            int totalWeight = 0;
                            for (int k = 0; k < localBoneWeights.Count; k++)
                                totalWeight += localBoneWeights[k];

                            if (totalWeight != 255)
                            {
                                // normalize remaining weights across the range
                                for (int k = 0; k < localBoneWeights.Count; k++)
                                {
                                    localBoneWeights[k] = (byte)(Math.Round((localBoneWeights[k] / (double)totalWeight) * 255));
                                    if (localBoneWeights[k] <= 0)
                                        localBoneIndices[k] = 0;
                                }

                                totalWeight = 0;
                                int maxWeight = 0;
                                int maxIndex = -1;

                                for (int k = 0; k < localBoneWeights.Count; k++)
                                {
                                    totalWeight += localBoneWeights[k];
                                    if (localBoneWeights[k] > maxWeight)
                                    {
                                        maxWeight = localBoneWeights[k];
                                        maxIndex = k;
                                    }
                                }
                                if (totalWeight != 255)
                                {
                                    localBoneWeights[maxIndex] = (byte)(maxWeight + (255 - totalWeight));
                                }
                            }

                            // sort by bone indices
                            finalBoneIndices = localBoneIndices.ToArray();
                            finalBoneWeights = localBoneWeights.ToArray();
                            Array.Sort(finalBoneIndices, finalBoneWeights);

                            localBoneIndices.Clear();
                            localBoneWeights.Clear();

                            // resize arrays to hold exactly 8
                            int origCount = finalBoneIndices.Length;
                            for (int z = 0; z < 8 - origCount; z++)
                                localBoneIndices.Add(finalBoneIndices[0]);
                            localBoneIndices.AddRange(finalBoneIndices);

                            localBoneWeights.AddRange(new byte[8 - origCount]);
                            localBoneWeights.AddRange(finalBoneWeights);

                            // finalize
                            finalBoneIndices = localBoneIndices.ToArray();
                            finalBoneWeights = localBoneWeights.ToArray();
                        }

                        vertex.SetValue("Pos", position);
                        vertex.SetValue("Binormal", binormal);
                        vertex.SetValue("Normal", normal);
                        vertex.SetValue("Tangent", tangent);

                        // add mandatory UV channel
                        {
                            FbxLayerElementUV layerUV = fmesh.GetElementUV(0, FbxLayerElement.EType.eUnknown);
                            if (layerUV != null)
                            {
                                int mappingIndex = (layerUV.MappingMode == EMappingMode.eByControlPoint) ? vertexIndex : (i * 3) + j;
                                int actualIndex = mappingIndex;
                                if (layerUV.ReferenceMode != EReferenceMode.eDirect)
                                    layerUV.IndexArray.GetAt(mappingIndex, out actualIndex);

                                layerUV.DirectArray.GetAt(actualIndex, out Vector2 uv);
                                vertex.SetValue("TexCoord" + 0, uv);
                            }
                        }

                        foreach (GeometryDeclarationDesc.Element elem in meshSection.GeometryDeclDesc[0].Elements)
                        {
                            switch (elem.Usage)
                            {
                                case VertexElementUsage.BoneIndices: vertex.SetValue("BoneIndices", finalBoneIndices); break;
                                case VertexElementUsage.BoneWeights: vertex.SetValue("BoneWeights", finalBoneWeights); break;

                                case VertexElementUsage.Color0:
                                    {
                                        FbxLayerElementVertexColor vc = fmesh.GetElementVertexColor(0);
                                        if (vc != null)
                                        {
                                            int mappingIndex = (vc.MappingMode == EMappingMode.eByControlPoint) ? vertexIndex : (i * 3) + j;
                                            int actualIndex = mappingIndex;
                                            if (vc.ReferenceMode != EReferenceMode.eDirect)
                                                vc.IndexArray.GetAt(mappingIndex, out actualIndex);

                                            vc.DirectArray.GetAt(actualIndex, out ColorBGRA color);
                                            vertex.SetValue("Color0", color);
                                        }
                                    }
                                    break;

                                case VertexElementUsage.Color1:
                                    {
                                        FbxLayerElementVertexColor vc = fmesh.GetElementVertexColor(1);
                                        if (vc != null)
                                        {
                                            int mappingIndex = (vc.MappingMode == EMappingMode.eByControlPoint) ? vertexIndex : (i * 3) + j;
                                            int actualIndex = mappingIndex;
                                            if (vc.ReferenceMode != EReferenceMode.eDirect)
                                                vc.IndexArray.GetAt(mappingIndex, out actualIndex);

                                            vc.DirectArray.GetAt(actualIndex, out ColorBGRA color);
                                            vertex.SetValue("Color1", color);
                                        }
                                    }
                                    break;

                                case VertexElementUsage.MaskUv:
                                    {
                                        FbxLayerElementUV layerUV = fmesh.GetElementUV("MaskUv");
                                        if (layerUV != null)
                                        {
                                            int mappingIndex = (layerUV.MappingMode == EMappingMode.eByControlPoint) ? vertexIndex : (i * 3) + j;
                                            int actualIndex = mappingIndex;
                                            if (layerUV.ReferenceMode != EReferenceMode.eDirect)
                                                layerUV.IndexArray.GetAt(mappingIndex, out actualIndex);

                                            layerUV.DirectArray.GetAt(actualIndex, out Vector2 uv);
                                            vertex.SetValue("MaskUv", uv);
                                        }
                                    }
                                    break;

                                case VertexElementUsage.Delta:
                                    {
                                        FbxLayerElementVertexColor layerColor = fmesh.GetElementVertexColor("Delta");
                                        if (layerColor != null)
                                        {
                                            int mappingIndex = (layerColor.MappingMode == EMappingMode.eByControlPoint) ? vertexIndex : (i * 3) + j;
                                            int actualIndex = mappingIndex;
                                            if (layerColor.ReferenceMode != EReferenceMode.eDirect)
                                                layerColor.IndexArray.GetAt(mappingIndex, out actualIndex);

                                            layerColor.DirectArray.GetAt(actualIndex, out ColorBGRA color);
                                            vertex.SetValue("Delta", color);
                                        }
                                    }
                                    break;

                                case VertexElementUsage.BlendWeights:
                                    {
                                        FbxLayerElementVertexColor layerColor = fmesh.GetElementVertexColor("BlendWeights");
                                        if (layerColor != null)
                                        {
                                            int mappingIndex = (layerColor.MappingMode == EMappingMode.eByControlPoint) ? vertexIndex : (i * 3) + j;
                                            int actualIndex = mappingIndex;
                                            if (layerColor.ReferenceMode != EReferenceMode.eDirect)
                                                layerColor.IndexArray.GetAt(mappingIndex, out actualIndex);

                                            layerColor.DirectArray.GetAt(actualIndex, out ColorBGRA color);
                                            vertex.SetValue("Delta", color.R / 255.0f);
                                        }
                                    }
                                    break;

                                case VertexElementUsage.RegionIds:
                                    {
                                        FbxLayerElementVertexColor layerColor = fmesh.GetElementVertexColor("RegionIds");
                                        if (layerColor != null)
                                        {
                                            int mappingIndex = (layerColor.MappingMode == EMappingMode.eByControlPoint) ? vertexIndex : (i * 3) + j;
                                            int actualIndex = mappingIndex;
                                            if (layerColor.ReferenceMode != EReferenceMode.eDirect)
                                                layerColor.IndexArray.GetAt(mappingIndex, out actualIndex);

                                            layerColor.DirectArray.GetAt(actualIndex, out ColorBGRA color);
                                            vertex.SetValue("Delta", (int)color.R);
                                        }
                                    }
                                    break;

                                case VertexElementUsage.SubMaterialIndex:
                                    {
                                        FbxLayerElementVertexColor layerColor = fmesh.GetElementVertexColor("SubMaterialIndex");
                                        if (layerColor != null)
                                        {
                                            int mappingIndex = (layerColor.MappingMode == EMappingMode.eByControlPoint) ? vertexIndex : (i * 3) + j;
                                            int actualIndex = mappingIndex;
                                            if (layerColor.ReferenceMode != EReferenceMode.eDirect)
                                            {
                                                layerColor.IndexArray.GetAt(mappingIndex, out actualIndex);
                                            }

                                            layerColor.DirectArray.GetAt(actualIndex, out ColorBGRA color);
                                            vertex.SetValue("SubMaterialIndex", color);
                                        }
                                    }
                                    break;

                                case VertexElementUsage.Pos:
                                case VertexElementUsage.BinormalSign:
                                case VertexElementUsage.Binormal:
                                case VertexElementUsage.Normal:
                                case VertexElementUsage.Tangent:
                                case VertexElementUsage.TangentSpace:
                                case VertexElementUsage.TexCoord0:
                                case VertexElementUsage.RadiosityTexCoord:
                                case VertexElementUsage.DisplacementMapTexCoord:
                                case VertexElementUsage.BoneIndices2:
                                case VertexElementUsage.BoneWeights2:
                                case VertexElementUsage.Unknown:
                                    break;

                                default:
                                    {
                                        if (elem.Usage >= VertexElementUsage.TexCoord1 && elem.Usage <= VertexElementUsage.TexCoord7)
                                        {
                                            int uvIndex = (int)(elem.Usage - VertexElementUsage.TexCoord0);
                                            FbxLayerElementUV layerUV = fmesh.GetElementUV(uvIndex, FbxLayerElement.EType.eUnknown);
                                            if (layerUV != null)
                                            {
                                                int mappingIndex = (layerUV.MappingMode == EMappingMode.eByControlPoint) ? vertexIndex : (i * 3) + j;
                                                int actualIndex = mappingIndex;
                                                if (layerUV.ReferenceMode != EReferenceMode.eDirect)
                                                    layerUV.IndexArray.GetAt(mappingIndex, out actualIndex);

                                                layerUV.DirectArray.GetAt(actualIndex, out Vector2 uv);
                                                vertex.SetValue("TexCoord" + uvIndex, uv);
                                            }
                                        }
                                        else
                                            throw new FBXImportUnimplementDataTypeException(elem.Usage);
                                    }
                                    break;
                            }
                        }

                        int foundIdx = -1;
                        if (indexToVertexMap[vertexIndex] != null)
                        {
                            foreach (int idx in indexToVertexMap[vertexIndex])
                            {
                                if (vertices[idx].GetValue<Vector4>("Pos") != vertex.GetValue<Vector4>("Pos"))
                                    continue;
                                if (vertices[idx].GetValue<Vector3>("Normal") != vertex.GetValue<Vector3>("Normal"))
                                    continue;
                                if (vertices[idx].GetValue<Vector3>("Tangent") != vertex.GetValue<Vector3>("Tangent"))
                                    continue;
                                if (vertices[idx].GetValue<Vector3>("Binormal") != vertex.GetValue<Vector3>("Binormal"))
                                    continue;

                                for (int k = 0; k < 8; k++)
                                {
                                    string channelName = "TexCoord" + k;
                                    if (vertex.HasValue(channelName))
                                    {
                                        if (vertices[idx].GetValue<Vector2>(channelName) != vertex.GetValue<Vector2>(channelName))
                                        {
                                            foundIdx = -2;
                                            break;
                                        }

                                    }
                                }

                                if (foundIdx == -1)
                                {
                                    foundIdx = idx;
                                    break;
                                }
                            }
                        }
                        else
                            indexToVertexMap[vertexIndex] = new List<int>();

                        if (foundIdx < 0)
                        {
                            if (vertices[vertexIndex] == null)
                            {
                                vertices[vertexIndex] = vertex;
                                indexToVertexMap[vertexIndex].Add(vertexIndex);
                                indices.Add(vertexIndex);
                            }
                            else
                            {
                                vertices.Add(vertex);
                                indexToVertexMap[vertexIndex].Add(vertices.Count - 1);
                                indices.Add(vertices.Count - 1);
                            }
                        }
                        else
                        {
                            indices.Add(foundIdx);
                        }
                    }
                }

                //if (foundBoneInfluences > totalBoneInfluences)
                //{
                //    // display warning about mismatching bones per vertex
                //    logger.LogWarning("Imported section '" + sectionNode.Name + "' contains " + foundBoneInfluences + " bones per vertex when the maximum for this section is " + totalBoneInfluences + " bones per vertex. Extra bone influences have been removed. This may result in incorrect skinning");
                //}

                // find and remove any redundant vertices
                List<int> indexToIndexMap = new List<int>(new int[vertices.Count]);
                int actualVertexId = 0;

                for (int vertexId = 0; vertexId < vertices.Count; vertexId++, actualVertexId++)
                {
                    if (vertices[vertexId] == null)
                    {
                        vertices.RemoveAt(vertexId);
                        vertexId--;
                    }
                    else
                    {
                        indexToIndexMap[actualVertexId] = vertexId;
                    }
                }

                // write new vertices
                using (NativeWriter chunkWriter = new NativeWriter(verticesBuffer, true))
                {
                    chunkWriter.Position = chunkWriter.Length;

                    for (int i = 0; i < meshSection.GeometryDeclDesc[0].Streams.Length; i++)
                    {
                        GeometryDeclarationDesc.Stream stream = meshSection.GeometryDeclDesc[0].Streams[i];
                        if (stream.VertexStride == 0)
                        {
                            continue;
                        }

                        foreach (DbObject vertex in vertices)
                        {
                            Vector4 tmp = vertex.GetValue<Vector4>("Pos");
                            Vector4 position = Vector3.Transform(new Vector3(tmp.X, tmp.Y, tmp.Z), sectionMatrix);

                            Vector3 normal = Vector3.TransformNormal(vertex.GetValue<Vector3>("Normal"), sectionMatrix);
                            Vector3 tangent = Vector3.TransformNormal(vertex.GetValue<Vector3>("Tangent"), sectionMatrix);
                            Vector3 binormal = Vector3.TransformNormal(vertex.GetValue<Vector3>("Binormal"), sectionMatrix);

                            // for some reason the tangent gets stored inverted
                            tangent *= -1.0f;

                            ushort[] finalBoneIndices = vertex.GetValue<ushort[]>("BoneIndices");
                            byte[] finalBoneWeights = vertex.GetValue<byte[]>("BoneWeights");

                            // for some reason some rigid meshes have bone indices and weights
                            // so we just make them all 0
                            if (finalBoneIndices == null)
                            {
                                finalBoneIndices = new ushort[8];
                            }
                            if (finalBoneWeights == null)
                            {
                                finalBoneWeights = new byte[8];
                            }

                            int currentStride = 0;
                            foreach (GeometryDeclarationDesc.Element elem in meshSection.GeometryDeclDesc[0].Elements)
                            {
                                if (elem.Usage == VertexElementUsage.Unknown)
                                    continue;

                                if (elem.StreamIndex == i && currentStride < stream.VertexStride)
                                {
                                    switch (elem.Usage)
                                    {
                                        case VertexElementUsage.Pos:
                                            {
                                                if (elem.Format == VertexElementFormat.Float3 || elem.Format == VertexElementFormat.Float4)
                                                {
                                                    chunkWriter.Write(position.X);
                                                    chunkWriter.Write(position.Y);
                                                    chunkWriter.Write(position.Z);

                                                    if (elem.Format == VertexElementFormat.Float4)
                                                        chunkWriter.Write(1.0f);
                                                }
                                                else if (elem.Format == VertexElementFormat.Half3 || elem.Format == VertexElementFormat.Half4)
                                                {
                                                    chunkWriter.Write(HalfUtils.Pack(position.X));
                                                    chunkWriter.Write(HalfUtils.Pack(position.Y));
                                                    chunkWriter.Write(HalfUtils.Pack(position.Z));

                                                    if (elem.Format == VertexElementFormat.Half4)
                                                        chunkWriter.Write(HalfUtils.Pack(1.0f));
                                                }
                                            }
                                            break;

                                        case VertexElementUsage.BinormalSign:
                                            {
                                                if (elem.Format == VertexElementFormat.Half)
                                                {
                                                    chunkWriter.Write(HalfUtils.Pack((Vector3.Dot(binormal, Vector3.Cross(normal, tangent))) < 0.0f ? 1.0f : -1.0f));
                                                }
                                                else if (elem.Format == VertexElementFormat.Half4 || elem.Format == VertexElementFormat.Float4)
                                                {
                                                    if (elem.Format == VertexElementFormat.Half4)
                                                    {
                                                        chunkWriter.Write(HalfUtils.Pack(tangent.X));
                                                        chunkWriter.Write(HalfUtils.Pack(tangent.Y));
                                                        chunkWriter.Write(HalfUtils.Pack(tangent.Z));
                                                        chunkWriter.Write(HalfUtils.Pack((Vector3.Dot(binormal, Vector3.Cross(normal, tangent))) < 0.0f ? 1.0f : -1.0f));
                                                    }
                                                    else
                                                    {
                                                        chunkWriter.Write(tangent.X);
                                                        chunkWriter.Write(tangent.Y);
                                                        chunkWriter.Write(tangent.Z);
                                                        chunkWriter.Write((Vector3.Dot(binormal, Vector3.Cross(normal, tangent))) < 0.0f ? 1.0f : -1.0f);
                                                    }
                                                }
                                                else
                                                {
                                                    chunkWriter.Write((Vector3.Dot(binormal, Vector3.Cross(normal, tangent))) < 0.0f ? 1.0f : -1.0f);
                                                }
                                            }
                                            break;

                                        case VertexElementUsage.BoneIndices:
                                            {
                                                if (elem.Format == VertexElementFormat.UShort4)
                                                {
                                                    chunkWriter.Write(finalBoneIndices[4]);
                                                    chunkWriter.Write(finalBoneIndices[5]);
                                                    chunkWriter.Write(finalBoneIndices[6]);
                                                    chunkWriter.Write(finalBoneIndices[7]);
                                                }
                                                else
                                                {
                                                    chunkWriter.Write((byte)finalBoneIndices[4]);
                                                    chunkWriter.Write((byte)finalBoneIndices[5]);
                                                    chunkWriter.Write((byte)finalBoneIndices[6]);
                                                    chunkWriter.Write((byte)finalBoneIndices[7]);
                                                }
                                            }
                                            break;

                                        case VertexElementUsage.BoneWeights:
                                            {
                                                chunkWriter.Write(finalBoneWeights[4]);
                                                chunkWriter.Write(finalBoneWeights[5]);
                                                chunkWriter.Write(finalBoneWeights[6]);
                                                chunkWriter.Write(finalBoneWeights[7]);
                                            }
                                            break;

                                        case VertexElementUsage.BoneIndices2:
                                            {
                                                if (elem.Format == VertexElementFormat.UShort4)
                                                {
                                                    chunkWriter.Write(finalBoneIndices[0]);
                                                    chunkWriter.Write(finalBoneIndices[1]);
                                                    chunkWriter.Write(finalBoneIndices[2]);
                                                    chunkWriter.Write(finalBoneIndices[3]);
                                                }
                                                else
                                                {
                                                    chunkWriter.Write((byte)finalBoneIndices[0]);
                                                    chunkWriter.Write((byte)finalBoneIndices[1]);
                                                    chunkWriter.Write((byte)finalBoneIndices[2]);
                                                    chunkWriter.Write((byte)finalBoneIndices[3]);
                                                }
                                            }
                                            break;

                                        case VertexElementUsage.BoneWeights2:
                                            {
                                                chunkWriter.Write(finalBoneWeights[0]);
                                                chunkWriter.Write(finalBoneWeights[1]);
                                                chunkWriter.Write(finalBoneWeights[2]);
                                                chunkWriter.Write(finalBoneWeights[3]);
                                            }
                                            break;

                                        case VertexElementUsage.Binormal:
                                            {
                                                chunkWriter.Write(HalfUtils.Pack(binormal.X));
                                                chunkWriter.Write(HalfUtils.Pack(binormal.Y));
                                                chunkWriter.Write(HalfUtils.Pack(binormal.Z));
                                                chunkWriter.Write(HalfUtils.Pack(1.0f));
                                            }
                                            break;

                                        case VertexElementUsage.Normal:
                                            {
                                                chunkWriter.Write(HalfUtils.Pack(normal.X));
                                                chunkWriter.Write(HalfUtils.Pack(normal.Y));
                                                chunkWriter.Write(HalfUtils.Pack(normal.Z));
                                                chunkWriter.Write(HalfUtils.Pack(1.0f));
                                            }
                                            break;

                                        case VertexElementUsage.Tangent:
                                            {
                                                chunkWriter.Write(HalfUtils.Pack(tangent.X));
                                                chunkWriter.Write(HalfUtils.Pack(tangent.Y));
                                                chunkWriter.Write(HalfUtils.Pack(tangent.Z));
                                                chunkWriter.Write(HalfUtils.Pack(1.0f));
                                            }
                                            break;

                                        case VertexElementUsage.TangentSpace:
                                            {
                                                if (elem.Format == VertexElementFormat.UInt)
                                                {
                                                    // packed as a packed quaternion

                                                    Vector3 b = Vector3.Cross(normal, tangent);
                                                    Quaternion quat = new Quaternion
                                                    {
                                                        X = normal.Y - binormal.Z,
                                                        Y = tangent.Z - normal.X,
                                                        Z = binormal.X - tangent.Y,
                                                        W = 1.0f + tangent.X + binormal.Y + normal.Z
                                                    };
                                                    quat.Normalize();

                                                    int idx = FindGreatestComponent(quat);
                                                    if (idx == 0) quat = new Quaternion(quat.W, quat.X, quat.Y, quat.Z);
                                                    if (idx == 1) quat = new Quaternion(quat.X, quat.W, quat.Y, quat.Z);
                                                    if (idx == 2) quat = new Quaternion(quat.X, quat.Y, quat.W, quat.Z);

                                                    Vector3 tmpQuat = new Vector3(quat.X, quat.Y, quat.Z) * Math.Sign(quat.W) * (float)Math.Sqrt(0.5f) + 0.5f;
                                                    Vector4 packedQuat = new Vector4(tmpQuat.X, tmpQuat.Y, tmpQuat.Z, idx / 3.0f);

                                                    uint ts = 0;
                                                    ts |= ((uint)(packedQuat.X * 1023.0f) << 22);
                                                    ts |= ((uint)(packedQuat.Y * 511.0f) << 13);
                                                    ts |= ((uint)(packedQuat.Z * 1023.0f) << 3);
                                                    ts |= ((uint)(packedQuat.W * 3.0f) << 1);
                                                    ts |= (uint)((Vector3.Dot(Vector3.Cross(normal, tangent), binormal)) < 0.0f ? 1 : 0);

                                                    chunkWriter.Write(ts);
                                                }
                                                else
                                                {
                                                    // axis angle normals (packed as either UByte4N or UShort4N)
                                                    // @todo
                                                }
                                            }
                                            break;

                                        case VertexElementUsage.RadiosityTexCoord:
                                            {
                                                chunkWriter.Write(HalfUtils.Pack(0.0f));
                                                chunkWriter.Write(HalfUtils.Pack(0.0f));
                                            }
                                            break;

                                        case VertexElementUsage.Color0:
                                            {
                                                if (vertex.HasValue("Color0"))
                                                {
                                                    ColorBGRA color = vertex.GetValue<ColorBGRA>("Color0");
                                                    chunkWriter.Write(color.R);
                                                    chunkWriter.Write(color.G);
                                                    chunkWriter.Write(color.B);
                                                    chunkWriter.Write(color.A);
                                                }
                                                else
                                                    chunkWriter.Write(0);
                                            }
                                            break;

                                        case VertexElementUsage.Color1:
                                            {
                                                if (vertex.HasValue("Color1"))
                                                {
                                                    ColorBGRA color = vertex.GetValue<ColorBGRA>("Color0");
                                                    chunkWriter.Write(color.R);
                                                    chunkWriter.Write(color.G);
                                                    chunkWriter.Write(color.B);
                                                    chunkWriter.Write(color.A);
                                                }
                                                else
                                                    chunkWriter.Write(0);
                                            }
                                            break;

                                        case VertexElementUsage.MaskUv:
                                            {
                                                if (vertex.HasValue("MaskUv"))
                                                {
                                                    Vector2 uv = vertex.GetValue<Vector2>("MaskUv");
                                                    chunkWriter.Write((short)((uv.X * 2 - 1) * short.MaxValue));
                                                    chunkWriter.Write((short)((uv.Y * 2 - 1) * short.MaxValue));
                                                }
                                                else
                                                {
                                                    chunkWriter.Write((ushort)0);
                                                    chunkWriter.Write((ushort)0);
                                                }
                                            }
                                            break;

                                        case VertexElementUsage.Delta:
                                            {
                                                if (vertex.HasValue("Delta"))
                                                {
                                                    ColorBGRA color = vertex.GetValue<ColorBGRA>("Delta");
                                                    chunkWriter.Write(HalfUtils.Pack(color.R * 255.0f));
                                                    chunkWriter.Write(HalfUtils.Pack(color.G * 255.0f));
                                                    chunkWriter.Write(HalfUtils.Pack(color.B * 255.0f));
                                                    chunkWriter.Write(HalfUtils.Pack(color.A * 255.0f));
                                                }
                                                else
                                                {
                                                    chunkWriter.Write(HalfUtils.Pack(0));
                                                    chunkWriter.Write(HalfUtils.Pack(0));
                                                    chunkWriter.Write(HalfUtils.Pack(0));
                                                    chunkWriter.Write(HalfUtils.Pack(0));
                                                }
                                            }
                                            break;

                                        case VertexElementUsage.BlendWeights:
                                            {
                                                if (vertex.HasValue("BlendWeights"))
                                                {
                                                    float x = vertex.GetValue<float>("BlendWeights");
                                                    chunkWriter.Write(x);
                                                }
                                                else
                                                {
                                                    chunkWriter.Write(0.0f);
                                                }
                                            }
                                            break;

                                        case VertexElementUsage.RegionIds:
                                            {
                                                if (vertex.HasValue("RegionIds"))
                                                {
                                                    int x = vertex.GetValue<int>("RegionIds");
                                                    chunkWriter.Write(x);
                                                }
                                                else
                                                {
                                                    chunkWriter.Write(0);
                                                }
                                            }
                                            break;

                                        case VertexElementUsage.SubMaterialIndex:
                                            {
                                                if (vertex.HasValue("SubMaterialIndex"))
                                                {
                                                    ColorBGRA subMaterialIndex = vertex.GetValue<ColorBGRA>("SubMaterialIndex");
                                                    chunkWriter.Write(subMaterialIndex.R);
                                                    chunkWriter.Write(subMaterialIndex.G);
                                                    chunkWriter.Write(subMaterialIndex.B);
                                                    chunkWriter.Write(subMaterialIndex.A);
                                                }
                                                else
                                                {
                                                    chunkWriter.Write(0);
                                                }
                                            }
                                            break;

                                        case VertexElementUsage.Unknown:
                                            break;

                                        default:
                                            {
                                                if (elem.Usage >= VertexElementUsage.TexCoord0 && elem.Usage <= VertexElementUsage.TexCoord7)
                                                {
                                                    string channelName = "TexCoord" + (int)(elem.Usage - VertexElementUsage.TexCoord0);
                                                    if (vertex.HasValue(channelName))
                                                    {
                                                        Vector2 uv = vertex.GetValue<Vector2>(channelName);
                                                        chunkWriter.Write(HalfUtils.Pack(uv.X));
                                                        chunkWriter.Write(HalfUtils.Pack(1.0f - uv.Y));
                                                    }
                                                    else
                                                    {
                                                        chunkWriter.Write((ushort)0);
                                                        chunkWriter.Write((ushort)0);
                                                    }
                                                }
                                                else
                                                    throw new FBXImportUnimplementDataTypeException(elem.Usage);
                                            }
                                            break;
                                    }

                                    currentStride += elem.Size;
                                }
                            }

                            // rivals pads the vertex stride
                            if (currentStride != stream.VertexStride)
                            {
                                chunkWriter.Position += stream.VertexStride - currentStride;
                            }
                        }
                    }

                    meshSection.VertexCount += (uint)vertices.Count;
                }

                // write indices
                int numIndices = 0;
                for (int i = 0; i < indices.Count; i++)
                {
                    indicesBuffer.Add((uint)(indexOffset + indexToIndexMap[indices[i]]));
                    numIndices++;
                }

                //generate AABB for vertices
                List<Vector3> vertexPositions = new List<Vector3>();
                foreach (DbObject vertex in vertices)
                {
                    Vector4 tmp = vertex.GetValue<Vector4>("Pos");
                    Vector4 position = Vector3.Transform(new Vector3(tmp.X, tmp.Y, tmp.Z), sectionMatrix);
                    vertexPositions.Add(new Vector3(position.X, position.Y, position.Z));

                }
                boundingBox = AABBFromPoints(vertexPositions).Item2;
                //meshSet.BoundingBox = AABBFromPoints(vertexPositions).Item1;

                // generate part bounding box
                /*if(meshSet.Type == MeshType.MeshType_Composite)
                {
                    List<Vector3> verticePositions = new List<Vector3>();
                    foreach (DbObject vertex in vertices)
                    {
                        Vector4 tmp = vertex.GetValue<Vector4>("Pos");
                        Vector4 position = Vector3.Transform(new Vector3(tmp.X, tmp.Y, tmp.Z), sectionMatrix);
                        verticePositions.Add(new Vector3(position.X, position.Y, position.Z));
                    }

                    //partBoundingBoxes.Add(AABBFromPoints(verticePositions));

                    // TEMP
                    // For now, just set the part indice to include everything
                    int partIndice = 0;
                    for (int i = 0; i < boneList.Count; i++)
                    {
                        partIndice |= 1 << i;
                    }
                    partIndices.Add(partIndice);
                }*/
                meshSection.PrimitiveCount += (uint)(numIndices / 3);

                startIndex += (uint)numIndices;
                indexOffset += (uint)vertices.Count;
            }

        }

        private (AxisAlignedBox, BoundingBox) AABBFromPoints(List<Vector3> points)
        {
            BoundingBox bbox = BoundingBox.FromPoints(points.ToArray());

            Vec3 min = new Vec3();
            min.x = bbox.Minimum.X;
            min.y = bbox.Minimum.Y;
            min.z = bbox.Minimum.Z;

            Vec3 max = new Vec3();
            max.x = bbox.Maximum.X;
            max.y = bbox.Maximum.Y;
            max.z = bbox.Maximum.Z;

            AxisAlignedBox aabb = new AxisAlignedBox();
            aabb.min = min;
            aabb.max = max;
            return (aabb, bbox);
        }
    }
}
