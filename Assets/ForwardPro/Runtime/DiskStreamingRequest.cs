using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.IO.LowLevel.Unsafe;

namespace UnityEngine.Rendering.EasyProbeVolume
{
    public class DiskStreamingRequest
    {
        ReadHandle m_ReadHandle;
        ReadCommandArray m_ReadCommandArray = new ReadCommandArray();
        NativeArray<ReadCommand> m_ReadCommandBuffer;
        int m_BytesWritten;
        
        public DiskStreamingRequest(int maxRequestCount)
        {
            m_ReadCommandBuffer = new NativeArray<ReadCommand>(maxRequestCount, Allocator.Persistent);
        }

        public bool ResizeIfNeeded(int maxRequestCount)
        {
            if (m_ReadCommandBuffer.Length < maxRequestCount)
            {
                Clear();
                Dispose();
                m_ReadCommandBuffer = new NativeArray<ReadCommand>(maxRequestCount, Allocator.Persistent);
                return true;
            }

            return false;
        }

        unsafe public void AddReadCommand(int offset, int size, byte* dest)
        {
            Debug.Assert(m_ReadCommandArray.CommandCount < m_ReadCommandBuffer.Length);

            m_ReadCommandBuffer[m_ReadCommandArray.CommandCount++] = new ReadCommand()
            {
                Buffer = dest,
                Offset = offset,
                Size = size
            };

            m_BytesWritten += size;
        }

        unsafe public int RunCommands(FileHandle file)
        {
            m_ReadCommandArray.ReadCommands = (ReadCommand*)m_ReadCommandBuffer.GetUnsafePtr();
            m_ReadHandle = AsyncReadManager.Read(file, m_ReadCommandArray);

            return m_BytesWritten;
        }

        public void Clear()
        {
            if (m_ReadHandle.IsValid())
                m_ReadHandle.JobHandle.Complete();
            m_ReadHandle = default;
            m_ReadCommandArray.CommandCount = 0;
            m_BytesWritten = 0;
        }

        public void Cancel()
        {
            if (m_ReadHandle.IsValid())
                m_ReadHandle.Cancel();
        }

        public void Wait()
        {
            if (m_ReadHandle.IsValid())
                m_ReadHandle.JobHandle.Complete();
        }

        public void Dispose()
        {
            m_ReadCommandBuffer.Dispose();
        }

        public ReadStatus GetStatus()
        {
            return m_ReadHandle.IsValid() ? m_ReadHandle.Status : ReadStatus.Complete;
        }
    }
}