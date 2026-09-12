using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace SuzerainAccess.Core
{
    /// <summary>
    /// Verifies that the installed GameAssembly.dll / global-metadata.dat are byte-identical to the files
    /// this mod was built and verified against. Every class, field and method the mod uses was checked
    /// against that exact build. Hashing runs on a background thread so startup is not delayed.
    /// </summary>
    internal static class VersionCheck
    {
        /// <summary>MD5 of the GameAssembly.dll supplied for development (75,134,464 bytes).</summary>
        public const string ExpectedGameAssemblyMd5 = "4966c5a0e8d51db95efddcd280cf2c3b";
        /// <summary>MD5 of the matching global-metadata.dat (17,847,660 bytes).</summary>
        public const string ExpectedMetadataMd5 = "320a4959bca3dfd31455d0c0545561f6";

        public enum Result { Pending, Match, Mismatch, Unknown }

        private static volatile Result _result = Result.Pending;
        private static volatile string _details = "";

        public static Result Status => _result;
        public static string Details => _details;

        public static void Start(string gameRoot)
        {
            Task.Run(() =>
            {
                try
                {
                    string asm = Path.Combine(gameRoot, "GameAssembly.dll");
                    if (!File.Exists(asm))
                    {
                        _details = "GameAssembly.dll not found at " + asm;
                        _result = Result.Unknown;
                        return;
                    }

                    // Unity names the data folder "<ExeName>_Data"; find it instead of assuming the name.
                    string meta = null;
                    foreach (var dir in Directory.GetDirectories(gameRoot, "*_Data"))
                    {
                        string candidate = Path.Combine(dir, "il2cpp_data", "Metadata", "global-metadata.dat");
                        if (File.Exists(candidate)) { meta = candidate; break; }
                    }

                    string asmHash = Md5(asm);
                    string metaHash = meta != null ? Md5(meta) : null;
                    bool asmOk = string.Equals(asmHash, ExpectedGameAssemblyMd5, StringComparison.OrdinalIgnoreCase);
                    bool metaOk = metaHash == null || string.Equals(metaHash, ExpectedMetadataMd5, StringComparison.OrdinalIgnoreCase);
                    _details = $"GameAssembly.dll MD5 {asmHash} (expected {ExpectedGameAssemblyMd5}); " +
                               (metaHash == null
                                   ? "global-metadata.dat not found"
                                   : $"global-metadata.dat MD5 {metaHash} (expected {ExpectedMetadataMd5})");
                    _result = asmOk && metaOk ? Result.Match : Result.Mismatch;
                }
                catch (Exception ex)
                {
                    _details = "Hash check failed: " + ex.Message;
                    _result = Result.Unknown;
                }
            });
        }

        private static string Md5(string path)
        {
            using var md5 = MD5.Create();
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20);
            return BitConverter.ToString(md5.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }
    }
}
