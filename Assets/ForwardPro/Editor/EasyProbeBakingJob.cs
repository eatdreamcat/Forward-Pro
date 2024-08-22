using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace UnityEngine.Rendering.EasyProbeVolume
{
    public static class EasyProbeBakingJob
    {
        [BurstCompile]
        struct BakingJob : IJobParallelFor
        {
            [NativeDisableContainerSafetyRestrictionAttribute]
            public NativeArray<float> coefficients;
            [NativeDisableContainerSafetyRestrictionAttribute]
            public NativeArray<float> probeAtten;
            [NativeDisableContainerSafetyRestrictionAttribute]
            public NativeArray<float> probeVisiable;
            
            [ReadOnly]
            public NativeArray<Vector3Int> probePosition;
            [ReadOnly]
            public NativeArray<EasyProbeLightSource> lightSources;

            public int sampleCount;

            public void Execute(int probeIndex)
            {
                for (int lightIndex = 0; lightIndex < lightSources.Length; ++ lightIndex)
                {
                    EasyProbeBakingUtils.BakeProbe(
                        lightSources[lightIndex],
                        probePosition[probeIndex],
                        probeAtten,
                        probeVisiable,
                        coefficients,
                        sampleCount,
                        probeIndex,
                        lightIndex, lightSources.Length);
                }
            }
            
        }
        
        public static JobHandle BakingProbe(
            NativeArray<float> coefficients,
            NativeArray<float> probeAtten,
            NativeArray<float> probeVisiable,
            NativeArray<Vector3Int> probePosition,
            NativeArray<EasyProbeLightSource> lightSources,
            int probeCount,
            int sampleCount
            )
        {
            BakingJob job = new BakingJob
            {
                coefficients = coefficients,
                probeAtten = probeAtten,
                probeVisiable = probeVisiable,
                probePosition = probePosition,
                lightSources = lightSources,
                sampleCount = sampleCount,
            };
            
            return job.Schedule(probeCount, lightSources.Length);
        }

        [BurstCompile]
        struct ProbeComponentSeperateJob : IJobParallelFor
        {
            public NativeArray<half4> shArDst;
            public NativeArray<half4> shAgDst;
            public NativeArray<half4> shAbDst;
            public NativeArray<half4> shBrDst;
            public NativeArray<half4> shBgDst;
            public NativeArray<half4> shBbDst;
            public NativeArray<half4> shCDst;
            [ReadOnly]
            public NativeArray<float> coefficientSrc;
            public void Execute(int probeIndex)
            {
                int coefficientBaseIndex = probeIndex * 27;
                // l0l1
                var l0r = (half)coefficientSrc[coefficientBaseIndex];
                var l1r0 = (half)coefficientSrc[coefficientBaseIndex + 3];
                var l1r1 = (half)coefficientSrc[coefficientBaseIndex+ 6];
                var l1r2 = (half)coefficientSrc[coefficientBaseIndex + 9];
                shArDst[probeIndex] = new half4(l1r0, l1r1, l1r2, l0r);
                var l0g = (half)coefficientSrc[coefficientBaseIndex + 1];
                var l1g0 = (half)coefficientSrc[coefficientBaseIndex + 4];
                var l1g1 = (half)coefficientSrc[coefficientBaseIndex + 7];
                var l1g2 = (half)coefficientSrc[coefficientBaseIndex + 10];
                shAgDst[probeIndex] = new half4(l1g0, l1g1, l1g2, l0g);
                var l0b = (half)coefficientSrc[coefficientBaseIndex + 2];
                var l1b0 = (half)coefficientSrc[coefficientBaseIndex + 5];
                var l1b1 = (half)coefficientSrc[coefficientBaseIndex + 8];
                var l1b2 = (half)coefficientSrc[coefficientBaseIndex + 11];
                shAbDst[probeIndex] = new half4(l1b0, l1b1, l1b2, l0b);
                // l2
                var l2_1thxyr = (half)coefficientSrc[coefficientBaseIndex + 12];
                var l2_1thyzr = (half)coefficientSrc[coefficientBaseIndex + 15];
                var l2_1thzzr = (half)coefficientSrc[coefficientBaseIndex + 18];
                var l2_1thzxr = (half)coefficientSrc[coefficientBaseIndex + 21];
                shBrDst[probeIndex] = new half4(l2_1thxyr, l2_1thyzr, l2_1thzzr, l2_1thzxr);
                var l2_1thxyg = (half)coefficientSrc[coefficientBaseIndex + 13];
                var l2_1thyzg = (half)coefficientSrc[coefficientBaseIndex + 16];
                var l2_1thzzg = (half)coefficientSrc[coefficientBaseIndex + 19];
                var l2_1thzxg = (half)coefficientSrc[coefficientBaseIndex + 22];
                shBgDst[probeIndex] = new half4(l2_1thxyg, l2_1thyzg, l2_1thzzg, l2_1thzxg);
                var l2_1thxyb = (half)coefficientSrc[coefficientBaseIndex + 14];
                var l2_1thyzb = (half)coefficientSrc[coefficientBaseIndex + 17];
                var l2_1thzzb = (half)coefficientSrc[coefficientBaseIndex + 20];
                var l2_1thzxb = (half)coefficientSrc[coefficientBaseIndex + 23];
                shBbDst[probeIndex] = new half4(l2_1thxyb, l2_1thyzb, l2_1thzzb, l2_1thzxb);
                var l2_5thr = (half)coefficientSrc[coefficientBaseIndex + 24];
                var l2_5thg = (half)coefficientSrc[coefficientBaseIndex + 25];
                var l2_5thb = (half)coefficientSrc[coefficientBaseIndex + 26];
                shCDst[probeIndex] = new half4(l2_5thr, l2_5thg, l2_5thb,(half)1.0f);
            }
        }
        
       
        public static JobHandle SeperateProbeComponent(
            NativeArray<half4> shArDst,
            NativeArray<half4> shAgDst,
            NativeArray<half4> shAbDst,
            NativeArray<half4> shBrDst,
            NativeArray<half4> shBgDst,
            NativeArray<half4> shBbDst,
            NativeArray<half4> shCDst,
            NativeArray<float> coefficientSrc,
            int probeCount
        )
        {
            ProbeComponentSeperateJob job = new ProbeComponentSeperateJob()
            {
                shArDst = shArDst,
                shAgDst = shAgDst,
                shAbDst = shAbDst,
                shBrDst = shBrDst,
                shBgDst = shBgDst,
                shBbDst = shBbDst,
                shCDst = shCDst,
                coefficientSrc = coefficientSrc,
            };
            
            return job.Schedule(probeCount, 64);
        }
        
        
         [BurstCompile]
        struct CombinedProbeJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<float> coefficientFlattenSrc;
            [NativeDisableContainerSafetyRestrictionAttribute]
            public NativeArray<float> coefficients;

            public int sampleCount;
            public void Execute(int probeIndex)
            {
                int probeCoefficientBaseIndexPerProbe = probeIndex * 27;
                int probeCoefficientBaseIndex = probeCoefficientBaseIndexPerProbe * sampleCount;
                
                for (int coefficientIndex = 0; coefficientIndex < 27; coefficientIndex += 3)
                {
                    Vector3 coefficientSum = Vector3.zero;
                    for (int sampleIndex = 0; sampleIndex < sampleCount; ++sampleIndex)
                    {
                        coefficientSum += new Vector3(
                            coefficientFlattenSrc[probeCoefficientBaseIndex + sampleIndex * 27 + coefficientIndex],
                            coefficientFlattenSrc[probeCoefficientBaseIndex + sampleIndex * 27 + coefficientIndex + 1],
                            coefficientFlattenSrc[probeCoefficientBaseIndex + sampleIndex * 27 + coefficientIndex + 2]);
                    
                    }

                    coefficients[probeCoefficientBaseIndexPerProbe + coefficientIndex] = coefficientSum.x;
                    coefficients[probeCoefficientBaseIndexPerProbe + coefficientIndex + 1] = coefficientSum.y;
                    coefficients[probeCoefficientBaseIndexPerProbe + coefficientIndex + 2] = coefficientSum.z;
                }
                
                
            }
        }
        public static JobHandle CombineProbeData(
            int probeCount, int sampleCount,
            NativeArray<float> coefficientFlattenSrc,
            NativeArray<float> coefficients
            )
        {
            CombinedProbeJob job = new CombinedProbeJob()
            { 
                coefficientFlattenSrc = coefficientFlattenSrc, 
                coefficients = coefficients,
                sampleCount = sampleCount
            };

            return job.Schedule(probeCount, 64);
        }

        [BurstCompile]
        struct FattenDataToPerCellDataJob : IJobParallelFor
        {
            public int cellCountPerSlice;
            public int cellCountPerLine;
            public int probeCountPerCellAxis;
            public Vector3Int probeCountPerVolumeAxis;
            
            [ReadOnly]
            public NativeArray<half4> shAr;
            [ReadOnly]
            public NativeArray<half4> shAg;
            [ReadOnly]
            public NativeArray<half4> shAb;
            [ReadOnly]
            public NativeArray<half4> shBr;
            [ReadOnly]
            public NativeArray<half4> shBg;
            [ReadOnly]
            public NativeArray<half4> shBb;
            [ReadOnly]
            public NativeArray<half4> shC;

            [NativeDisableContainerSafetyRestrictionAttribute]
            public NativeArray<half4> l0l1PerCell;
            [NativeDisableContainerSafetyRestrictionAttribute]
            public NativeArray<half4> l2PerCell;
            
            public void Execute(int cellIndex)
            {
                var sliceIndex = cellIndex / cellCountPerSlice;
                var rowIndex = (cellIndex - (sliceIndex * cellCountPerSlice)) / cellCountPerLine;
                var columnIndex = cellIndex % cellCountPerLine;

                var probeCountPerCell = cellIndex *
                                        probeCountPerCellAxis * probeCountPerCellAxis * probeCountPerCellAxis;
                var dataBaseIndexL0L1 = probeCountPerCell * 3;
                var dataBaseIndexL2 = probeCountPerCell * 4;
                
                var probeCountPerSlice = probeCountPerVolumeAxis.x * probeCountPerVolumeAxis.y;
                var probeBaseIndex = sliceIndex * (probeCountPerCellAxis - 1) * probeCountPerSlice
                                     + rowIndex * (probeCountPerCellAxis - 1) * probeCountPerVolumeAxis.x 
                                     + columnIndex * (probeCountPerCellAxis - 1);
                
                for (int probeSliceIndex = 0; probeSliceIndex < probeCountPerCellAxis; ++probeSliceIndex)
                {
                    for (int probeRowIndex = 0; probeRowIndex < probeCountPerCellAxis; ++probeRowIndex)
                    {
                        for (int probeColumnIndex = 0; probeColumnIndex < probeCountPerCellAxis; ++probeColumnIndex)
                        {
                            var probeIndex = probeBaseIndex + probeSliceIndex * probeCountPerSlice
                                                            + probeRowIndex * probeCountPerVolumeAxis.x
                                                            + probeColumnIndex;
                            
                            
                            l0l1PerCell[dataBaseIndexL0L1] = shAr[probeIndex];
                            l0l1PerCell[dataBaseIndexL0L1 + 1] = shAg[probeIndex];
                            l0l1PerCell[dataBaseIndexL0L1 + 2] = shAb[probeIndex];
                            
                            l2PerCell[dataBaseIndexL2] = shBr[probeIndex];
                            l2PerCell[dataBaseIndexL2 + 1] = shBg[probeIndex];
                            l2PerCell[dataBaseIndexL2 + 2] = shBb[probeIndex];
                            l2PerCell[dataBaseIndexL2 + 3] = shC[probeIndex];
                        }
                    }
                }
            }
        }
        
        public static JobHandle FlattenDataToPerCellData(
            int probeCountPerCellAxis, Vector3Int probeCountPerVolumeAxis, 
            NativeArray<half4> shAr, 
            NativeArray<half4> shAg, 
            NativeArray<half4> shAb, 
            NativeArray<half4> shBr, 
            NativeArray<half4> shBg, 
            NativeArray<half4> shBb, 
            NativeArray<half4> shC,
            NativeArray<half4> l0l1PerCell,
            NativeArray<half4> l2PerCell,
            int cellCount,
            Vector3Int cellCountPerAxis
        )
        {
            FattenDataToPerCellDataJob job = new FattenDataToPerCellDataJob()
            { 
                probeCountPerCellAxis =  probeCountPerCellAxis,
                probeCountPerVolumeAxis = probeCountPerVolumeAxis,
                shAr = shAr,
                shAg = shAg,
                shAb = shAb,
                shBr = shBr,
                shBg = shBg,
                shBb = shBb,
                shC = shC,
                l0l1PerCell = l0l1PerCell,
                l2PerCell = l2PerCell,
                cellCountPerSlice = cellCountPerAxis.x * cellCountPerAxis.y,
                cellCountPerLine = cellCountPerAxis.x
            };

            return job.Schedule(cellCount, 64);
        }
    }
}
