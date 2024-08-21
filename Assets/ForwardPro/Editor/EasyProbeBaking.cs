using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;

namespace UnityEngine.Rendering.EasyProbeVolume
{
    public static class EasyProbeBaking
    {
        private static string s_CurrentOutputRoot = "";
        private static HashSet<Vector3Int> s_TempProbeCellTags = new();
        private static Dictionary<Vector3Int, int> s_TempProbeTags = new();
        private static List<EasyCellData> s_TempCellMetadata = new();

        public static List<EasyProbe> s_Probes = new();
        public static List<EasyProbeCell> s_ProbeCells = new();
        
        public static int s_ProbeSpacing = 1;
        public static int s_ProbeCellSize = 2;
        public static int s_MaxProbeSpacing = 10;
        public static int s_MaxProbeCellSize = 30;

        private static NativeArray<float> s_ProbeCoefficientsFlatten;
        private static NativeArray<float> s_ProbeCoefficients;
        private static NativeArray<Vector3Int> s_ProbePosition;
        private static NativeArray<float> s_ProbeAttenFlatten;
        private static NativeArray<float> s_ProbeVisibilityFlatten;
        private static NativeArray<EasyProbeLightSource> s_Lights;
        
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

            s_Probes.Clear();
            s_TempProbeTags.Clear();

            foreach (var cell in s_ProbeCells)
            {
                PlaceCellProbes(cell);
            }
        }

        static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                foreach (string file in Directory.GetFiles(path))
                {
                    File.Delete(file);
                }
                
                foreach (string subDirectory in Directory.GetDirectories(path))
                {
                    DeleteDirectory(subDirectory);
                }
                
                Directory.Delete(path);
            }
        }

        static bool NeedBake(EasyProbe probe, EasyProbeLightSource lightSource)
        {
            foreach (var cell in probe.cells)
            {
                if (lightSource.IntersectCell(cell.bounds))
                {
                    return true;
                }
            }

            return false;
        }
    
        public static void Bake(List<EasyProbeLightSource> lightSources, int sampleCount)
        {
            if (!PrepareBaking())
            {
                return;
            }

            EasyProbeStreaming.Dispose();
            
            var currentScenePath = SceneManagement.SceneManager.GetActiveScene().path;
            var lastIndexOfSep = currentScenePath.LastIndexOf("/");
            currentScenePath = currentScenePath.Substring(0, lastIndexOfSep);

            var output = currentScenePath + EasyProbeStreaming.s_OutputDir;
            s_CurrentOutputRoot = currentScenePath;
            DeleteDirectory(output);
            Directory.CreateDirectory(output);
            AssetDatabase.Refresh();
            
            SortByWorldPositionXYZ();
            
            {
                if (s_ProbeCoefficientsFlatten.IsCreated)
                {
                    s_ProbeCoefficientsFlatten.Dispose();
                }
                
                s_ProbeCoefficientsFlatten = new NativeArray<float>(27 * s_Probes.Count * sampleCount, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);

                if (s_ProbePosition.IsCreated)
                {
                    s_ProbePosition.Dispose();
                }
                
                s_ProbePosition = new NativeArray<Vector3Int>(s_Probes.Count, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < s_Probes.Count; ++i)
                {
                    s_ProbePosition[i] = s_Probes[i].position;
                }
                s_ProbeAttenFlatten = new NativeArray<float>(s_Probes.Count * lightSources.Count, Allocator.Persistent);
                s_ProbeVisibilityFlatten = new NativeArray<float>(s_Probes.Count * lightSources.Count, Allocator.Persistent);

                if (s_Lights.IsCreated)
                {
                    s_Lights.Dispose();
                }
                
                s_Lights = new NativeArray<EasyProbeLightSource>(lightSources.Count, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);

                for (int i = 0; i < lightSources.Count; ++i)
                {
                    s_Lights[i] = lightSources[i];
                }

                var bakingHandle = EasyProbeBakingJob.BakingProbe(
                    s_ProbeCoefficientsFlatten,
                    s_ProbeAttenFlatten,
                    s_ProbeVisibilityFlatten,
                    s_ProbePosition,
                    s_Lights,
                    s_Probes.Count,
                    sampleCount
                    );
                
                bakingHandle.Complete();

                if (s_ProbeCoefficients.IsCreated)
                {
                    s_ProbeCoefficients.Dispose();
                }
                
                s_ProbeCoefficients = new NativeArray<float>(27 * s_Probes.Count, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
                
                var combineHandle = EasyProbeBakingJob.CombineProbeData(
                    s_Probes.Count, sampleCount,
                    s_ProbeCoefficientsFlatten,
                    s_ProbeCoefficients
                );
                combineHandle.Complete();
            }

            if (EasyProbeStreaming.s_DataStorageType == DataStorageType.Flatten)
            {
                WriteOutputFlatten();
            }
            else
            {
                WriteOutputPerCell();
            }
            
            EasyProbeStreaming.Dispose();
        }

        static void SortByWorldPositionXYZ()
        {
            s_ProbeCells.Sort((p1, p2) =>
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
            
            s_Probes.Sort((p1, p2) =>
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
        
        static void SeperateProbesComponent()
        {
            var handle = EasyProbeBakingJob.SeperateProbeComponent(
                s_SHAr,
                s_SHAg,
                s_SHAb,
                s_SHBr,
                s_SHBg,
                s_SHBb,
                s_SHC,
                s_ProbeCoefficients,
                s_Probes.Count
            );
            
            handle.Complete();
        }

        static void AllocBufferData(int width, int height, int depth)
        {
            if (s_SHAr.IsCreated) s_SHAr.Dispose();
            s_SHAr = new NativeArray<half4>(width * height * depth, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            if (s_SHAg.IsCreated) s_SHAg.Dispose();
            s_SHAg = new NativeArray<half4>(width * height * depth, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            if (s_SHAb.IsCreated) s_SHAb.Dispose();
            s_SHAb = new NativeArray<half4>(width * height * depth, Allocator.Persistent,
                            NativeArrayOptions.UninitializedMemory);
            if (s_SHBr.IsCreated) s_SHBr.Dispose();
            s_SHBr = new NativeArray<half4>(width * height * depth, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            if (s_SHBg.IsCreated) s_SHBg.Dispose();
            s_SHBg = new NativeArray<half4>(width * height * depth, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            if (s_SHBb.IsCreated) s_SHBb.Dispose();
            s_SHBb = new NativeArray<half4>(width * height * depth, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            if (s_SHC.IsCreated) s_SHC.Dispose();
            s_SHC = new NativeArray<half4>(width * height * depth, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
        }

        static void PrepareForOutputWriting(out Vector3 probeCountPerAxis, out Vector3Int cellMin, out Vector3Int cellMax)
        {
            cellMin = s_ProbeCells[0].Min;
            cellMax = s_ProbeCells[s_ProbeCells.Count - 1].Max;
            var halfSize = s_ProbeSpacing / 2.0f;
            EasyProbeStreaming.s_ProbeVolumeWorldOffset = 
                new Vector4(cellMin.x - halfSize, cellMin.y - halfSize, cellMin.z - halfSize, 1.0f);
            EasyProbeStreaming.s_ProbeVolumeSize = cellMax - cellMin;
            EasyProbeStreaming.s_ProbeVolumeSize += new Vector3(
                s_ProbeSpacing,
                s_ProbeSpacing,
                s_ProbeSpacing
            );
            
            probeCountPerAxis = EasyProbeStreaming.s_ProbeVolumeSize / s_ProbeSpacing;
                
            Debug.Assert(probeCountPerAxis.x * probeCountPerAxis.y * probeCountPerAxis.z == s_Probes.Count);
            AllocBufferData((int)probeCountPerAxis.x, (int)probeCountPerAxis.y, (int)probeCountPerAxis.z);    
            
            SeperateProbesComponent();
        }
        
        static void WriteOutputPerCell()
        {
            PrepareForOutputWriting(out var probeCountPerAxis, out var cellMin, out var cellMax);
            
        }
        
        static void WriteOutputFlatten()
        {
            PrepareForOutputWriting(out var probeCountPerAxis, out var cellMin, out var cellMax);
            
            WriteBytesFlatten(probeCountPerAxis, cellMin, cellMax);
        }

        static byte[] StructToBytes<T>(T obj) where T : struct
        {
            int size = Marshal.SizeOf(obj);
            byte[] arr = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);

            try
            {
                Marshal.StructureToPtr(obj, ptr, true);
                Marshal.Copy(ptr, arr, 0, size);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }

            return arr;
        }
        
        static byte[] ListToByteArray<T>(List<T> list) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            byte[] byteArray = new byte[size * list.Count];
        
            for (int i = 0; i < list.Count; i++)
            {
                IntPtr ptr = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(list[i], ptr, true);
                    Marshal.Copy(ptr, byteArray, i * size, size);
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }

            return byteArray;
        }
        
        static void WriteBytesFlatten(Vector3 probeCountPerAxis, Vector3Int cellMin, Vector3Int cellMax)
        {
            var probeCountPerCellAxis = s_ProbeCellSize / s_ProbeSpacing + 1;
            // metadata
            {
                var metadata = new EasyProbeMetaData()
                {
                    cellMax = cellMax,
                    cellMin = cellMin,
                    probeCountPerCellAxis = probeCountPerCellAxis,
                    probeSpacing = s_ProbeSpacing,
                    cellSize = s_ProbeCellSize,
                    probeCountPerVolumeAxis = new Vector3Int((int)probeCountPerAxis.x, (int)probeCountPerAxis.y, (int)probeCountPerAxis.z),
                };

                var metadataByte = StructToBytes(metadata);
                Debug.Assert(string.IsNullOrEmpty(s_CurrentOutputRoot) == false);
                string path = s_CurrentOutputRoot + EasyProbeStreaming.s_MetadataPath;
                File.WriteAllBytes(path, metadataByte);
                Debug.Log("[EasyProbeBaking](WriteBytes): meta data written to " + path);
                
            }

            int totalProbeCount = (int)(probeCountPerAxis.x * probeCountPerAxis.y * probeCountPerAxis.z);
            
            // SH L0L1
            {
                var l0l1Packed = new NativeArray<half4>(s_Probes.Count * 3, 
                    Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < totalProbeCount; ++i)
                {
                    l0l1Packed[i] = s_SHAr[i];
                    l0l1Packed[i + totalProbeCount] = s_SHAg[i];
                    l0l1Packed[i + 2 * totalProbeCount] = s_SHAb[i];
                }
                
                var l0l1ByteSlice = l0l1Packed.Slice(0, l0l1Packed.Length).SliceConvert<byte>();
                var bytesToWrite = new byte[l0l1ByteSlice.Length];
                l0l1ByteSlice.CopyTo(bytesToWrite);
                string path = s_CurrentOutputRoot + EasyProbeStreaming.s_L0L1DataPath;
                File.WriteAllBytes(path, bytesToWrite);
                l0l1Packed.Dispose();
                Debug.Log("[EasyProbeBaking](WriteBytes): l0l1 data written to " + path);
            }

            // SH L2
            {
                var l2Packed = new NativeArray<half4>(s_Probes.Count * 4, 
                    Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                
                for (int i = 0; i < totalProbeCount; ++i)
                {
                    l2Packed[i] = s_SHBr[i];
                    l2Packed[i + totalProbeCount] = s_SHBg[i];
                    l2Packed[i + 2 * totalProbeCount] = s_SHBb[i];
                    l2Packed[i + 3 * totalProbeCount] = s_SHC[i];
                }
                
                var l2ByteSlice = l2Packed.Slice(0, l2Packed.Length).SliceConvert<byte>();
                var bytesToWrite = new byte[l2ByteSlice.Length];
                l2ByteSlice.CopyTo(bytesToWrite);
                string path = s_CurrentOutputRoot + EasyProbeStreaming.s_L2DataPath;
                File.WriteAllBytes(path, bytesToWrite);
                l2Packed.Dispose();
                Debug.Log("[EasyProbeBaking](WriteBytes): l2 data written to " + path);
                
            }
            
            // CellData
            {
                // ReSharper disable once InvalidXmlDocComment
                /**
                 *  cellCountPerAxis * (cellSize / probeSpacing) + 1 = probeCountPerAxis
                 *  cellCountPerAxis = (probeCountPerAxis - 1) / (cellSize / probeSpacing)
                 */
                s_TempCellMetadata.Clear();
                var cellCountPerAxis = (probeCountPerAxis - Vector3.one) / ((float)s_ProbeCellSize / s_ProbeSpacing);
                Debug.Assert((int)(cellCountPerAxis.x * cellCountPerAxis.y * cellCountPerAxis.z) == s_ProbeCells.Count);
                var cellCountPerSlice = cellCountPerAxis.x * cellCountPerAxis.y;
                var probeCountPerSlice = probeCountPerAxis.x * probeCountPerAxis.y;
                for (int i = 0; i < s_ProbeCells.Count; ++i)
                {
                    var cell = s_ProbeCells[i];
                    int cellSlice = (int)(i / cellCountPerAxis.z);
                    var cellIndexInsideSlice = i - cellSlice * cellCountPerSlice;
                    int cellX = (int)(cellIndexInsideSlice % cellCountPerAxis.x);
                    int cellY = (int)(cellIndexInsideSlice / cellCountPerAxis.x);

                    // var probeStartPerSlice = cellSlice * probeCountPerSlice;
                    // var probeStartInsideSlice = cellY * probeCountPerAxis.x + cellX * (probeCountPerCellAxis - 1);
                    
                    s_TempCellMetadata.Add(new EasyCellData()
                    {
                        position = new Vector4(
                            cell.position.x,
                            cell.position.y,
                            cell.position.z, 
                            probeCountPerSlice),
                        cellIndex = new Vector4(
                            cellX,
                            cellY,
                            cellSlice,
                            i
                            ),
                    });
                }

                var bytesToWrite = ListToByteArray(s_TempCellMetadata);
                string path = s_CurrentOutputRoot + EasyProbeStreaming.s_CellDataPath;
                File.WriteAllBytes(path, bytesToWrite);
                Debug.Log("[EasyProbeBaking](WriteBytes): cell data written to " + path);
            }

            if (s_ProbeCoefficientsFlatten.IsCreated) s_ProbeCoefficientsFlatten.Dispose();
            if (s_ProbeCoefficients.IsCreated) s_ProbeCoefficients.Dispose();
            if (s_ProbeAttenFlatten.IsCreated) s_ProbeAttenFlatten.Dispose();
            if (s_ProbeVisibilityFlatten.IsCreated) s_ProbeVisibilityFlatten.Dispose();
            if (s_Lights.IsCreated) s_Lights.Dispose();
            if (s_ProbePosition.IsCreated) s_ProbePosition.Dispose();

            if (s_SHAr.IsCreated) s_SHAr.Dispose();
            if (s_SHAg.IsCreated) s_SHAg.Dispose();
            if (s_SHAb.IsCreated) s_SHAb.Dispose();
            if (s_SHBr.IsCreated) s_SHBr.Dispose();
            if (s_SHBg.IsCreated) s_SHBg.Dispose();
            if (s_SHBb.IsCreated) s_SHBb.Dispose();
            if (s_SHC.IsCreated) s_SHC.Dispose();
            
            AssetDatabase.Refresh();
        }

        static bool PrepareBaking()
        {
            PlaceProbes();
            return s_ProbeCells.Count > 0;
        }

        

        static void SubdivideCell()
        {
            s_TempProbeCellTags.Clear();
            s_ProbeCells.Clear();
            foreach (var volume in EasyProbeVolume.s_ProbeVolumes)
            {
                if (!volume.Valid)
                {
                    continue;
                }

                Vector3Int indexStart = EasyProbeStreaming.GetCellIndexStart(volume.Min, s_ProbeCellSize);
                Vector3Int indexEnd = EasyProbeStreaming.GetCellIndexEnd(volume.Max, s_ProbeCellSize);

                var step = s_ProbeCellSize;
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
                                size = s_ProbeCellSize,
                            };
                            s_ProbeCells.Add(cell);
                        }
                    }
                }
            }
        }

        static void PlaceCellProbes(EasyProbeCell cell)
        {
            var step = s_ProbeSpacing;
            Vector3Int probePositionStart = cell.Min;
            int perAxisCount = cell.size / s_ProbeSpacing + 1;
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
                            s_Probes[indice].cells.Add(cell);
                            continue;
                        }
                        
                        s_TempProbeTags.Add(position, s_Probes.Count);
                        var probe = new EasyProbe(position);
                        probe.cells.Add(cell);
                        s_Probes.Add(probe);
                    }
                }

            }
        }
    }
}
