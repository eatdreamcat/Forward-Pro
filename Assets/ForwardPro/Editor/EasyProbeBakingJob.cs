using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace UnityEngine.Rendering.EasyProbeVolume
{
    public static class EasyProbeBakingJob
    {
        [BurstCompile]
        struct BakingJob : IJobParallelFor
        {
            public NativeArray<float> coefficients;
            public NativeArray<float> probeAtten;
            public NativeArray<float> probeVisiable;
            public NativeArray<Vector3Int> probePosition;
            [ReadOnly]
            public NativeArray<EasyProbeLightSource> lightSources;

            public int sampleCount;

            public void Execute(int index)
            {
                foreach (var lightSource in lightSources)
                {
                    EasyProbeBakingUtils.BakeProbe(
                        lightSource,
                        probePosition[index],
                        ref probeAtten,
                        ref probeVisiable,
                        ref coefficients,
                        sampleCount,
                        index);
                }
            }
        }

        public static JobHandle BakingProbe(
            ref NativeArray<float> coefficients,
            ref NativeArray<float> probeAtten,
            ref NativeArray<float> probeVisiable,
            ref NativeArray<Vector3Int> probePosition,
            ref NativeArray<EasyProbeLightSource> lightSources
            )
        {
            BakingJob job = new BakingJob
            {
                coefficients = coefficients,
                probeAtten = probeAtten,
                probeVisiable = probeVisiable,
                probePosition = probePosition,
                lightSources = lightSources
            };

            return job.Schedule(probePosition.Length, 64);
        }
        
    }
}
