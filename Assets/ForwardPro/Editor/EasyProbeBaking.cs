using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.EasyProbeVolume
{
    public static class EasyProbeBaking
    {
        public static string s_OutputDir = "/EasyProbe";
        private static HashSet<Vector3Int> s_TempProbeCellTags = new();
        private static Dictionary<Vector3Int, int> s_TempProbeTags = new();

        private static NativeArray<half4> s_SHAr;
        private static NativeArray<half4> s_SHAg;
        private static NativeArray<half4> s_SHAb;
        private static NativeArray<half4> s_SHBr;
        private static NativeArray<half4> s_SHBg;
        private static NativeArray<half4> s_SHBb;
        private static NativeArray<half4> s_SHC;
        
        public static void PlaceProbes()
        {
            SubdivideCell();

            EasyProbeVolume.s_Probes.Clear();
            s_TempProbeTags.Clear();

            foreach (var cell in EasyProbeVolume.s_ProbeCells)
            {
                PlaceCellProbes(cell);
            }
        }

        static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                // 删除文件夹中的所有文件
                foreach (string file in Directory.GetFiles(path))
                {
                    File.Delete(file);
                }

                // 递归删除所有子文件夹
                foreach (string subDirectory in Directory.GetDirectories(path))
                {
                    DeleteDirectory(subDirectory);
                }

                // 删除文件夹本身
                Directory.Delete(path);
            }
        }

        static bool NeedBake(EasyProbe probe, EasyProbeLightSource lightSource)
        {
            foreach (var cellIndex in probe.cells)
            {
                var cell = EasyProbeVolume.s_ProbeCells[cellIndex];
                if (lightSource.IntersectCell(cell))
                {
                    return true;
                }
            }

            return false;
        }
    
        public static void Bake(List<EasyProbeLightSource> lightSources/*, EasyProbeVolumeEditor.SampleDirDensity sampleDirDensity*/, int sampleCount)
        {
            if (!PrepareBaking())
            {
                return;
            }

            var currentScenePath = SceneManagement.SceneManager.GetActiveScene().path;
            var lastIndexOfSep = currentScenePath.LastIndexOf("/");
            currentScenePath = currentScenePath.Substring(0, lastIndexOfSep);

            var outputPath = currentScenePath + s_OutputDir;
            DeleteDirectory(outputPath);
            Directory.CreateDirectory(outputPath);
            AssetDatabase.Refresh();

            foreach (var probe in EasyProbeVolume.s_Probes)
            {
                foreach (var lightSource in lightSources)
                {
                    if (NeedBake(probe, lightSource))
                    {
                        EasyProbeBakingUtils.BakeProbe(lightSource.light, probe, sampleCount);
                        // switch (sampleDirDensity)
                        // {
                        //     case EasyProbeVolumeEditor.SampleDirDensity._6:
                        //         EasyProbeBakingUtils.BakeProbe(lightSource.light, probe, 0, 6, sampleCount);
                        //         break;
                        //     case EasyProbeVolumeEditor.SampleDirDensity._8:
                        //         EasyProbeBakingUtils.BakeProbe(lightSource.light, probe, 6, 14, sampleCount);
                        //         break;
                        //     case EasyProbeVolumeEditor.SampleDirDensity._14:
                        //         EasyProbeBakingUtils.BakeProbe(lightSource.light, probe, 0, 14, sampleCount);
                        //         break;
                        // }
                    }
                }
            }
            
            WriteOutput();
        }

        static void SortByWorldPositionXYZ()
        {
            EasyProbeVolume.s_ProbeCells.Sort((p1, p2) =>
            {
                if (p1 == null || p2 == null)
                {
                    throw new ArgumentException("Cell can't be null");
                }
                
                int result = p1.position.z.CompareTo(p2.position.z);
                if (result != 0)
                {
                    return result;
                }
                
                result = p1.position.y.CompareTo(p2.position.y);
                if (result != 0)
                {
                    return result;
                }
                
                return p1.position.x.CompareTo(p2.position.x);
            });
            
            EasyProbeVolume.s_Probes.Sort((p1, p2) =>
            {
                if (p1 == null || p2 == null)
                {
                    throw new ArgumentException("Cell can't be null");
                }
                
                int result = p1.position.z.CompareTo(p2.position.z);
                if (result != 0)
                {
                    return result;
                }
                
                result = p1.position.y.CompareTo(p2.position.y);
                if (result != 0)
                {
                    return result;
                }
                
                return p1.position.x.CompareTo(p2.position.x);
            });
        }

        static void UpdateDataLocationTexture<T>(Texture output, NativeArray<T> input) where T : struct
        {
            var output3D = output as Texture3D;
            var outputNativeArray = output3D.GetPixelData<T>(0);
            Debug.Assert(outputNativeArray.Length >= input.Length);
            outputNativeArray.GetSubArray(0, input.Length).CopyFrom(input);
            (output as Texture3D).Apply();
        }
        
        static void FlattenProbes()
        {
            for(int i = 0; i < EasyProbeVolume.s_Probes.Count; ++i)
            {
                var probe = EasyProbeVolume.s_Probes[i];
                var l0r = (half)probe.coefficients[0];
                var l1r0 = (half)probe.coefficients[3];
                var l1r1 = (half)probe.coefficients[6];
                var l1r2 = (half)probe.coefficients[9];
                s_SHAr[i] = new half4(l1r0, l1r1, l1r2, l0r);
                var l0g = (half)probe.coefficients[1];
                var l1g0 = (half)probe.coefficients[4];
                var l1g1 = (half)probe.coefficients[7];
                var l1g2 = (half)probe.coefficients[10];
                s_SHAg[i] = new half4(l1g0, l1g1, l1g2, l0g);
                var l0b = (half)probe.coefficients[2];
                var l1b0 = (half)probe.coefficients[5];
                var l1b1 = (half)probe.coefficients[8];
                var l1b2 = (half)probe.coefficients[11];
                s_SHAb[i] = new half4(l1b0, l1b1, l1b2, l0b);
                var l2_1thxxr = (half)probe.coefficients[12];
                var l2_1thyzr = (half)probe.coefficients[15];
                var l2_1thzzr = (half)probe.coefficients[18];
                var l2_1thzxr = (half)probe.coefficients[21];
                s_SHBr[i] = new half4(l2_1thxxr, l2_1thyzr, l2_1thzzr, l2_1thzxr);
                var l2_1thxxg = (half)probe.coefficients[13];
                var l2_1thyzg = (half)probe.coefficients[16];
                var l2_1thzzg = (half)probe.coefficients[19];
                var l2_1thzxg = (half)probe.coefficients[22];
                s_SHBg[i] = new half4(l2_1thxxg, l2_1thyzg, l2_1thzzg, l2_1thzxg);
                var l2_1thxxb = (half)probe.coefficients[14];
                var l2_1thyzb = (half)probe.coefficients[17];
                var l2_1thzzb = (half)probe.coefficients[20];
                var l2_1thzxb = (half)probe.coefficients[23];
                s_SHBb[i] = new half4(l2_1thxxb, l2_1thyzb, l2_1thzzb, l2_1thzxb);
                var l2_5thr = (half)probe.coefficients[24];
                var l2_5thg = (half)probe.coefficients[25];
                var l2_5thb = (half)probe.coefficients[26];
                
                s_SHC[i] = new half4(l2_5thr, l2_5thg, l2_5thb,(half)1.0f);
            }
            
        }

        static void ApplyTextureData()
        {
            UpdateDataLocationTexture(EasyProbeStreaming.s_EasyProbeSHAr, s_SHAr);
            UpdateDataLocationTexture(EasyProbeStreaming.s_EasyProbeSHAg, s_SHAg);
            UpdateDataLocationTexture(EasyProbeStreaming.s_EasyProbeSHAb, s_SHAb);
            
            UpdateDataLocationTexture(EasyProbeStreaming.s_EasyProbeSHBr, s_SHBr);
            UpdateDataLocationTexture(EasyProbeStreaming.s_EasyProbeSHBg, s_SHBg);
            UpdateDataLocationTexture(EasyProbeStreaming.s_EasyProbeSHBb, s_SHBb);
            
            UpdateDataLocationTexture(EasyProbeStreaming.s_EasyProbeSHC, s_SHC);
        }
        
        static void AllocBufferData(int width, int height, int depth)
        {
            if (EasyProbeStreaming.s_EasyProbeSHAr != null)
            {
                CoreUtils.Destroy(EasyProbeStreaming.s_EasyProbeSHAr);
            }
            
            if (EasyProbeStreaming.s_EasyProbeSHAg != null)
            {
                CoreUtils.Destroy(EasyProbeStreaming.s_EasyProbeSHAg);
            }
            
            if (EasyProbeStreaming.s_EasyProbeSHAb != null)
            {
                CoreUtils.Destroy(EasyProbeStreaming.s_EasyProbeSHAb);
            }
            
            if (EasyProbeStreaming.s_EasyProbeSHBr != null)
            {
                CoreUtils.Destroy(EasyProbeStreaming.s_EasyProbeSHBr);
            }
            
            if (EasyProbeStreaming.s_EasyProbeSHBg != null)
            {
                CoreUtils.Destroy(EasyProbeStreaming.s_EasyProbeSHBg);
            }
            
            if (EasyProbeStreaming.s_EasyProbeSHBb != null)
            {
                CoreUtils.Destroy(EasyProbeStreaming.s_EasyProbeSHBb);
            }
            
            if (EasyProbeStreaming.s_EasyProbeSHC != null)
            {
                CoreUtils.Destroy(EasyProbeStreaming.s_EasyProbeSHC);
            }

            if (s_SHAr.IsCreated) s_SHAr.Dispose();
            if (s_SHAg.IsCreated) s_SHAg.Dispose();
            if (s_SHAb.IsCreated) s_SHAb.Dispose();
            
            if (s_SHBr.IsCreated) s_SHBr.Dispose();
            if (s_SHBg.IsCreated) s_SHBg.Dispose();
            if (s_SHBb.IsCreated) s_SHBb.Dispose();
            
            if (s_SHC.IsCreated) s_SHC.Dispose();

            s_SHAr = new NativeArray<half4>(width * height * depth, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            s_SHAg = new NativeArray<half4>(width * height * depth, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            s_SHAb = new NativeArray<half4>(width * height * depth, Allocator.Persistent,
                            NativeArrayOptions.UninitializedMemory);
            s_SHBr = new NativeArray<half4>(width * height * depth, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            s_SHBg = new NativeArray<half4>(width * height * depth, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            s_SHBb = new NativeArray<half4>(width * height * depth, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            s_SHC = new NativeArray<half4>(width * height * depth, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            
            int bytes = 0;
            EasyProbeStreaming.s_EasyProbeSHAr
                = EasyProbeStreaming.CreateDataTexture(width, height, 
                    depth, GraphicsFormat.R16G16B16A16_SFloat, "_EasyProbeSHAr", 
                    false, ref bytes);
            EasyProbeStreaming.s_EasyProbeSHAg
                = EasyProbeStreaming.CreateDataTexture(width, height,
                        depth, GraphicsFormat.R16G16B16A16_SFloat, "_EasyProbeSHAg", 
                        false, ref bytes);
            EasyProbeStreaming.s_EasyProbeSHAb
                = EasyProbeStreaming.CreateDataTexture(width, height,
                        depth, GraphicsFormat.R16G16B16A16_SFloat, "_EasyProbeSHAb", 
                        false, ref bytes);
            EasyProbeStreaming.s_EasyProbeSHBr
                = EasyProbeStreaming.CreateDataTexture(width, height,
                        depth, GraphicsFormat.R16G16B16A16_SFloat, "_EasyProbeSHBr", 
                        false, ref bytes);
            EasyProbeStreaming.s_EasyProbeSHBg
                = EasyProbeStreaming.CreateDataTexture(width, height,
                        depth, GraphicsFormat.R16G16B16A16_SFloat, "_EasyProbeSHBg", 
                        false, ref bytes);
            EasyProbeStreaming.s_EasyProbeSHBb
                = EasyProbeStreaming.CreateDataTexture(width, height,
                        depth, GraphicsFormat.R16G16B16A16_SFloat, "_EasyProbeSHBb", 
                        false, ref bytes);
            EasyProbeStreaming.s_EasyProbeSHC
                = EasyProbeStreaming.CreateDataTexture(width, height,
                        depth, GraphicsFormat.R16G16B16A16_SFloat, "_EasyProbeSHC", 
                        false, ref bytes);
        }
        
        static void WriteOutput()
        {
           
            SortByWorldPositionXYZ();
            {
                // TODO: should be removed
                // Test
                var cellMin = EasyProbeVolume.s_ProbeCells[0].Min;
                var cellMax = EasyProbeVolume.s_ProbeCells[EasyProbeVolume.s_ProbeCells.Count - 1].Max;
                var halfSize = EasyProbeVolume.s_ProbeSpacing / 2.0f;
             
                EasyProbeStreaming.s_ProbeVolumeWorldOffset = 
                    new Vector4(cellMin.x - halfSize, cellMin.y - halfSize, cellMin.z - halfSize, 1.0f);
                EasyProbeStreaming.s_ProbeVolumeSize = cellMax - cellMin;
                EasyProbeStreaming.s_ProbeVolumeSize += new Vector3(
                    EasyProbeVolume.s_ProbeSpacing,
                    EasyProbeVolume.s_ProbeSpacing,
                    EasyProbeVolume.s_ProbeSpacing
                );
                var probeCountPerAxie = EasyProbeStreaming.s_ProbeVolumeSize / EasyProbeVolume.s_ProbeSpacing;
                
                Debug.Assert(probeCountPerAxie.x * probeCountPerAxie.y * probeCountPerAxie.z == EasyProbeVolume.s_Probes.Count);
                AllocBufferData((int)probeCountPerAxie.x, (int)probeCountPerAxie.y, (int)probeCountPerAxie.z);
            }
            
            FlattenProbes();
            
            ApplyTextureData();
        }

        static bool PrepareBaking()
        {
            if (EasyProbeVolume.s_ProbeCells.Count <= 0 || EasyProbeVolume.s_Probes.Count <= 0)
            {
                PlaceProbes();
            }

            return EasyProbeVolume.s_ProbeCells.Count > 0;
        }

        static Vector3Int GetCellIndexStart(EasyProbeVolume volume)
        {
            var index = volume.Min / EasyProbeVolume.s_ProbeCellSize;
            return new Vector3Int(
                Mathf.FloorToInt(index.x),
                Mathf.FloorToInt(index.y),
                Mathf.FloorToInt(index.z)
            );
        }

        static Vector3Int GetCellIndexEnd(EasyProbeVolume volume)
        {
            var index = volume.Max / EasyProbeVolume.s_ProbeCellSize;
            return new Vector3Int(
                Mathf.CeilToInt(index.x),
                Mathf.CeilToInt(index.y),
                Mathf.CeilToInt(index.z)
            );
        }

        static void SubdivideCell()
        {
            s_TempProbeCellTags.Clear();
            EasyProbeVolume.s_ProbeCells.Clear();
            foreach (var volume in EasyProbeVolume.s_ProbeVolumes)
            {
                if (!volume.Valid)
                {
                    continue;
                }

                Vector3Int indexStart = GetCellIndexStart(volume);
                Vector3Int indexEnd = GetCellIndexEnd(volume);

                var step = EasyProbeVolume.s_ProbeCellSize;
                for (int x = indexStart.x; x < indexEnd.x; ++x)
                {
                    for (int y = indexStart.y; y < indexEnd.y; ++y)
                    {
                        for (int z = indexStart.z; z < indexEnd.z; ++z)
                        {
                            var position = new Vector3Int(
                                x * step + step / 2,
                                y * step + step / 2,
                                z * step + step / 2
                            );

                            if (s_TempProbeCellTags.Contains(position))
                            {
                                continue;
                            }

                            s_TempProbeCellTags.Add(position);

                            var cell = new EasyProbeCell()
                            {
                                position = position,
                                size = EasyProbeVolume.s_ProbeCellSize,
                                index = EasyProbeVolume.s_ProbeCells.Count
                            };
                            EasyProbeVolume.s_ProbeCells.Add(cell);
                        }
                    }
                }
            }
        }

        static void PlaceCellProbes(EasyProbeCell cell)
        {
            var step = EasyProbeVolume.s_ProbeSpacing;
            Vector3Int probePositionStart = cell.Min;
            int perAxisCount = cell.size / EasyProbeVolume.s_ProbeSpacing + 1;
            for (int x = 0; x < perAxisCount; ++x)
            {
                for (int y = 0; y < perAxisCount; ++y)
                {
                    for (int z = 0; z < perAxisCount; ++z)
                    {
                        var position = new Vector3Int(
                            x * step,
                            y * step,
                            z * step
                        );

                        position += probePositionStart;
                        
                        if (s_TempProbeTags.TryGetValue(position, out var indice))
                        {
                            cell.probeIndices.Add(indice);
                            EasyProbeVolume.s_Probes[indice].cells.Add(cell.index);
                            continue;
                        }
                        
                        s_TempProbeTags.Add(position, EasyProbeVolume.s_Probes.Count);
                        var probe = new EasyProbe(position);
                        probe.cells.Add(cell.index);
                        EasyProbeVolume.s_Probes.Add(probe);
                    }
                }

            }
        }
    }
}
