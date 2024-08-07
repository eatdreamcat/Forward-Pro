using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityEngine.Rendering.EasyProbeVolume
{
    public static class EasyProbeBaking
    {
        public static string s_OutputDir = "/EasyProbe";
        public static List<EasyProbeCell> s_ProbeCells = new();
        private static HashSet<Vector3Int> s_TempProbeCellTags = new();

        public static List<EasyProbe> s_Probes = new();
        private static Dictionary<Vector3Int, int> s_TempProbeTags = new();

        static EasyProbeBaking()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
            RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
        }

        static void BeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera.cameraType != CameraType.SceneView)
            {
                return;
            }


        }

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
                var cell = s_ProbeCells[cellIndex];
                if (lightSource.IntersectCell(cell))
                {
                    return true;
                }
            }

            return false;
        }
    
        public static void Bake(List<EasyProbeLightSource> lightSources, EasyProbeVolumeEditor.SampleDirDensity sampleDirDensity, int sampleCount)
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

            foreach (var probe in s_Probes)
            {
                foreach (var lightSource in lightSources)
                {
                    // if (NeedBake(probe, lightSource))
                    {
                        switch (sampleDirDensity)
                        {
                            case EasyProbeVolumeEditor.SampleDirDensity._6:
                                EasyProbeBakingUtils.BakeProbe(lightSource.light, probe, 0, 6, sampleCount);
                                break;
                            case EasyProbeVolumeEditor.SampleDirDensity._8:
                                EasyProbeBakingUtils.BakeProbe(lightSource.light, probe, 6, 14, sampleCount);
                                break;
                            case EasyProbeVolumeEditor.SampleDirDensity._14:
                                EasyProbeBakingUtils.BakeProbe(lightSource.light, probe, 0, 14, sampleCount);
                                break;
                        }
                    }
                }
            }
        }


        static bool PrepareBaking()
        {
            if (s_ProbeCells.Count <= 0 || s_Probes.Count <= 0)
            {
                PlaceProbes();
            }

            return s_ProbeCells.Count > 0;
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
            s_ProbeCells.Clear();
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
                                index = s_ProbeCells.Count
                            };
                            s_ProbeCells.Add(cell);
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
                            s_Probes[indice].cells.Add(cell.index);
                            continue;
                        }
                        
                        s_TempProbeTags.Add(position, s_Probes.Count);
                        var probe = new EasyProbe(position);
                        probe.cells.Add(cell.index);
                        s_Probes.Add(probe);
                    }
                }

            }
        }
    }
}
