using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Polyfork.EditorTools
{
    /// <summary>
    /// Just enough tar to unpack a PuerTS release.
    ///
    /// .NET has had `System.Formats.Tar` since 7, which Unity's runtime is not, so gzip is
    /// available and tar is not. Rather than take a dependency or shell out to the `tar`
    /// binary - present on modern Windows, but a process launch is a portability question
    /// this package has so far avoided - it reads the format directly. Tar is 512-byte
    /// headers and padded payloads; the whole of what is needed here fits in one file.
    ///
    /// Scoped to what these archives actually contain, verified by inspecting them: regular
    /// files and directories only, no symlinks or hard links, and every path under 100
    /// characters so no long-name records. PAX extended headers appear and are skipped,
    /// which is correct precisely because no path needs them.
    /// </summary>
    static class PolyforkTar
    {
        const int BlockSize = 512;

        public static void ExtractTarGz(byte[] archive, string destination)
        {
            using var raw = new MemoryStream(archive, writable: false);
            using var gzip = new GZipStream(raw, CompressionMode.Decompress);
            Extract(gzip, destination);
        }

        static void Extract(Stream tar, string destination)
        {
            var header = new byte[BlockSize];

            // Trailing separator matters: without it "/out" also prefixes "/out-elsewhere",
            // and the traversal check below would wave it through.
            var full = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar)
                       + Path.DirectorySeparatorChar;

            while (true)
            {
                if (!ReadExactly(tar, header, BlockSize)) return;
                if (IsAllZero(header)) return;                 // end-of-archive marker

                var name = ReadString(header, 0, 100);
                var prefix = ReadString(header, 345, 155);     // ustar splits long paths here
                if (prefix.Length > 0) name = prefix + "/" + name;

                var size = ReadOctal(header, 124, 12);
                var type = (char)header[156];
                var padded = (size + BlockSize - 1) / BlockSize * BlockSize;

                // 'x' and 'g' are PAX metadata, 'L'/'K' are GNU long name records. None
                // carry file content worth keeping here, so their payload is skipped.
                if (type is 'x' or 'g' or 'L' or 'K' || string.IsNullOrEmpty(name))
                {
                    Skip(tar, padded);
                    continue;
                }

                /* An archive entry is untrusted input, and "../" in a name is how a tarball
                 * writes outside the folder it was told to unpack into. Resolve first, then
                 * refuse anything that escaped. */
                var path = Path.GetFullPath(Path.Combine(destination, name));
                if (!path.StartsWith(full, StringComparison.Ordinal))
                    throw new IOException($"tar entry escapes the destination: {name}");

                if (type == '5')
                {
                    Directory.CreateDirectory(path);
                    Skip(tar, padded);
                    continue;
                }

                if (type != '0' && type != '\0')   // links, devices, anything else
                {
                    Skip(tar, padded);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? destination);
                WriteFile(tar, path, size);
                Skip(tar, padded - size);
            }
        }

        static void WriteFile(Stream tar, string path, long size)
        {
            using var file = File.Create(path);

            var buffer = new byte[64 * 1024];
            var left = size;

            while (left > 0)
            {
                var want = (int)Math.Min(buffer.Length, left);
                var got = tar.Read(buffer, 0, want);
                if (got <= 0) throw new EndOfStreamException($"tar ended inside {path}");

                file.Write(buffer, 0, got);
                left -= got;
            }
        }

        /// <summary>Fills the buffer, or returns false at a clean end of stream.</summary>
        static bool ReadExactly(Stream stream, byte[] buffer, int count)
        {
            var offset = 0;
            while (offset < count)
            {
                var got = stream.Read(buffer, offset, count - offset);
                if (got <= 0) return offset != 0 ? throw new EndOfStreamException("truncated tar header") : false;
                offset += got;
            }
            return true;
        }

        static void Skip(Stream stream, long count)
        {
            if (count <= 0) return;

            // GZipStream is forward-only, so seeking is not available: read and discard.
            var buffer = new byte[(int)Math.Min(count, 64 * 1024)];
            while (count > 0)
            {
                var got = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
                if (got <= 0) return;
                count -= got;
            }
        }

        static bool IsAllZero(byte[] block)
        {
            foreach (var b in block)
            {
                if (b != 0) return false;
            }
            return true;
        }

        static string ReadString(byte[] block, int offset, int length)
        {
            var end = offset;
            var limit = offset + length;
            while (end < limit && block[end] != 0) end++;

            return Encoding.UTF8.GetString(block, offset, end - offset).Trim();
        }

        /// <summary>Tar stores numbers as octal ASCII, space- or NUL-terminated.</summary>
        static long ReadOctal(byte[] block, int offset, int length)
        {
            long value = 0;
            for (var i = offset; i < offset + length; i++)
            {
                var c = block[i];
                if (c is 0 or (byte)' ') break;
                if (c < '0' || c > '7') continue;

                value = value * 8 + (c - '0');
            }
            return value;
        }
    }
}
