using System.Runtime.InteropServices;

namespace DiskPartitionInfo.Extensions
{
    internal static class ByteArrayExtensions
    {
        internal static T ToStruct<T>(this byte[] bytes)
            where T : struct
        {
            T result;
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);

            try
            {
                result = Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject())!;
            }
            finally
            {
                handle.Free();
            }

            return result;
        }
    }
}
