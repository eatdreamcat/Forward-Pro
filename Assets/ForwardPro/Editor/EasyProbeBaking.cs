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
        
        public static int s_ProbeSpacing = 2;
        public static int s_ProbeCellSize = 6;
        public static float s_PointAttenConstantK = 0.1f;
        public static int s_MaxProbeSpacing = 10;
        public static int s_MaxProbeCellSize = 30;
        
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
                if (lightSource.IntersectCell(cell))
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

            foreach (var probe in s_Probes)
            {
                foreach (var lightSource in lightSources)
                {
                    if (NeedBake(probe, lightSource))
                    {
                        EasyProbeBakingUtils.BakeProbe(lightSource.light, probe, sampleCount);
                    }
                }
            }
            
            WriteOutput();
            
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
        
        static void FlattenProbes()
        {
            for(int i = 0; i < s_Probes.Count; ++i)
            {
                // l0l1
                var probe = s_Probes[i];
                var l0r = (half)probe.coefficients[0];
                var l1r0 = (half)probe.coefficients[3];
                var l1r1 = (half)probe.coefficients[6];
                var l1r2 = (half)probe.coefficients[9];
                EasyProbeStreaming.s_SHAr[i] = new half4(l1r0, l1r1, l1r2, l0r);
                var l0g = (half)probe.coefficients[1];
                var l1g0 = (half)probe.coefficients[4];
                var l1g1 = (half)probe.coefficients[7];
                var l1g2 = (half)probe.coefficients[10];
                EasyProbeStreaming.s_SHAg[i] = new half4(l1g0, l1g1, l1g2, l0g);
                var l0b = (half)probe.coefficients[2];
                var l1b0 = (half)probe.coefficients[5];
                var l1b1 = (half)probe.coefficients[8];
                var l1b2 = (half)probe.coefficients[11];
                EasyProbeStreaming.s_SHAb[i] = new half4(l1b0, l1b1, l1b2, l0b);
                // l2
                var l2_1thxyr = (half)probe.coefficients[12];
                var l2_1thyzr = (half)probe.coefficients[15];
                var l2_1thzzr = (half)probe.coefficients[18];
                var l2_1thzxr = (half)probe.coefficients[21];
                EasyProbeStreaming.s_SHBr[i] = new half4(l2_1thxyr, l2_1thyzr, l2_1thzzr, l2_1thzxr);
                var l2_1thxyg = (half)probe.coefficients[13];
                var l2_1thyzg = (half)probe.coefficients[16];
                var l2_1thzzg = (half)probe.coefficients[19];
                var l2_1thzxg = (half)probe.coefficients[22];
                EasyProbeStreaming.s_SHBg[i] = new half4(l2_1thxyg, l2_1thyzg, l2_1thzzg, l2_1thzxg);
                var l2_1thxyb = (half)probe.coefficients[14];
                var l2_1thyzb = (half)probe.coefficients[17];
                var l2_1thzzb = (half)probe.coefficients[20];
                var l2_1thzxb = (half)probe.coefficients[23];
                EasyProbeStreaming.s_SHBb[i] = new half4(l2_1thxyb, l2_1thyzb, l2_1thzzb, l2_1thzxb);
                var l2_5thr = (half)probe.coefficients[24];
                var l2_5thg = (half)probe.coefficients[25];
                var l2_5thb = (half)probe.coefficients[26];
                
                EasyProbeStreaming.s_SHC[i] = new half4(l2_5thr, l2_5thg, l2_5thb,(half)1.0f);
            }
            
        }

        static void AllocTempBufferData(int width, int height, int depth)
        {
            EasyProbeStreaming.s_SHAr = new NativeArray<half4>(width * height * depth, Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            EasyProbeStreaming.s_SHAg = new NativeArray<half4>(width * height * depth, Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            EasyProbeStreaming.s_SHAb = new NativeArray<half4>(width * height * depth, Allocator.Temp,
                            NativeArrayOptions.UninitializedMemory);
            EasyProbeStreaming.s_SHBr = new NativeArray<half4>(width * height * depth, Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            EasyProbeStreaming.s_SHBg = new NativeArray<half4>(width * height * depth, Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            EasyProbeStreaming.s_SHBb = new NativeArray<half4>(width * height * depth, Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            EasyProbeStreaming.s_SHC = new NativeArray<half4>(width * height * depth, Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
        }
        
        static void WriteOutput()
        {
           
            SortByWorldPositionXYZ();
            
            var cellMin = s_ProbeCells[0].Min;
            var cellMax = s_ProbeCells[s_ProbeCells.Count - 1].Max;
            var halfSize = s_ProbeSpacing / 2.0f;
            EasyProbeStreaming.s_ProbeVolumeWorldOffset = 
                new Vector4(cellMin.x - halfSize, cellMin.y - halfSize, cellMin.z - halfSize, 1.0f);
            EasyProbeStreaming.s_ProbeVolumeSize = cellMax - cellMin;
            EasyProbeStreaming.s_ProbeVolumeSize += new Vector3(
                s_ProbeSpacing,
                s_ProbeSpacing,
                s_ProbeSpacing
            );
            var probeCountPerAxis = EasyProbeStreaming.s_ProbeVolumeSize / s_ProbeSpacing;
                
            Debug.Assert(probeCountPerAxis.x * probeCountPerAxis.y * probeCountPerAxis.z == s_Probes.Count);
            AllocTempBufferData((int)probeCountPerAxis.x, (int)probeCountPerAxis.y, (int)probeCountPerAxis.z);    
            
            FlattenProbes();
            WriteBytes(probeCountPerAxis, cellMin, cellMax);
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
        
        static void WriteBytes(Vector3 probeCountPerAxis, Vector3Int cellMin, Vector3Int cellMax)
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
                    probeCountPerVolumeAxis = new Vector3Int((int)probeCountPerAxis.x, (int)probeCountPerAxis.y, (int)probeCountPerAxis.z)
                };

                var metadataByte = StructToBytes(metadata);
                Debug.Assert(string.IsNullOrEmpty(s_CurrentOutputRoot) == false);
                string path = s_CurrentOutputRoot + EasyProbeStreaming.s_MetadataPath;
                File.WriteAllBytes(path, metadataByte);
                Debug.Log("[EasyProbeBaking](WriteBytes): meta data written to " + path);
                
            }
            
            // SH L0L1
            {
                var l0l1Packed = new NativeArray<half4>(s_Probes.Count * 3, 
                    Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < l0l1Packed.Length; i+=3)
                {
                    l0l1Packed[i] = EasyProbeStreaming.s_SHAr[i / 3];
                    l0l1Packed[i + 1] = EasyProbeStreaming.s_SHAg[i / 3];
                    l0l1Packed[i + 2] = EasyProbeStreaming.s_SHAr[i / 3];
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
                for (int i = 0; i < l2Packed.Length; i+=4)
                {
                    l2Packed[i] = EasyProbeStreaming.s_SHBr[i / 4];
                    l2Packed[i + 1] = EasyProbeStreaming.s_SHBg[i / 4];
                    l2Packed[i + 2] = EasyProbeStreaming.s_SHBb[i / 4];
                    l2Packed[i + 3] = EasyProbeStreaming.s_SHC[i / 4];
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
                        // probeDataPerSliceLayoutL0L1 = new Vector4(
                        //     ),
                        // probeDataPerSliceLayoutL2 = new Vector4(
                        //     ),
                        // probeDataSliceLayout = new Vector4(
                        //     )
                    });
                }

                var bytesToWrite = ListToByteArray(s_TempCellMetadata);
                string path = s_CurrentOutputRoot + EasyProbeStreaming.s_CellDataPath;
                File.WriteAllBytes(path, bytesToWrite);
                Debug.Log("[EasyProbeBaking](WriteBytes): cell data written to " + path);
            }
            
            AssetDatabase.Refresh();
        }

        static bool PrepareBaking()
        {
            if (s_ProbeCells.Count <= 0 || s_Probes.Count <= 0)
            {
                PlaceProbes();
            }

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
