using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Jiten.Core.Utils;
using WanaKanaShaapu;

namespace Jiten.Parser;

static class SudachiInterop
{
    private delegate IntPtr RunCliFfiDelegate(string configPath, string filePath, string dictionaryPath, string outputPath);

    private delegate IntPtr ProcessTextFfiDelegate(string configPath, IntPtr inputText, string dictionaryPath, char mode, bool printAll,
                                                   bool wakati);

    private delegate void FreeStringDelegate(IntPtr ptr);

    private static RunCliFfiDelegate _runCliFfi;
    private static ProcessTextFfiDelegate _processTextFfi;
    private static FreeStringDelegate _freeString;

    private static readonly IntPtr _libHandle;

    private static string GetSudachiLibPath()
    {
        string basePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Path.Combine(basePath, "sudachi_lib.dll");
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return Path.Combine(basePath, "libsudachi_lib.so");
        else
            throw new PlatformNotSupportedException("Unsupported platform");
    }

    static SudachiInterop()
    {
        // Load the appropriate native library for the current platform
        _libHandle = NativeLibrary.Load(GetSudachiLibPath());

        // Get function pointers
        IntPtr runCliFfiPtr = NativeLibrary.GetExport(_libHandle, "run_cli_ffi");
        IntPtr processTextFfiPtr = NativeLibrary.GetExport(_libHandle, "process_text_ffi");
        IntPtr freeStringPtr = NativeLibrary.GetExport(_libHandle, "free_string");

        // Create delegates from function pointers
        _runCliFfi = Marshal.GetDelegateForFunctionPointer<RunCliFfiDelegate>(runCliFfiPtr);
        _processTextFfi = Marshal.GetDelegateForFunctionPointer<ProcessTextFfiDelegate>(processTextFfiPtr);
        _freeString = Marshal.GetDelegateForFunctionPointer<FreeStringDelegate>(freeStringPtr);
    }

    private static readonly object ProcessTextLock = new object();


    public static string RunCli(string configPath, string filePath, string dictionaryPath, string outputPath)
    {
        // Call the FFI function
        IntPtr resultPtr = _runCliFfi(configPath, filePath, dictionaryPath, outputPath);

        // Convert the result to a C# string
        string result = Marshal.PtrToStringAnsi(resultPtr) ?? string.Empty;

        // Free the string allocated in Rust
        _freeString(resultPtr);

        return result;
    }

    public static string ProcessText(string configPath, string inputText, string dictionaryPath, char mode = 'C', bool printAll = true,
                                     bool wakati = false)
    {
        lock (ProcessTextLock)
        {
            // Clean up text
            inputText = Regex.Replace(inputText,
                                      "[^\u3040-\u309F\u30A0-\u30FF\u4E00-\u9FAF\uFF21-\uFF3A\uFF41-\uFF5A\uFF10-\uFF19\u3005\u3001-\u3003\u3008-\u3011\u3014-\u301F\uFF01-\uFF0F\uFF1A-\uFF1F\uFF3B-\uFF3F\uFF5B-\uFF60\uFF62-\uFF65．\\n…\u3000―\u2500() 」\\|]",
                                      "");

            // if there's no kanas or kanjis, abort
            if (WanaKana.IsRomaji(inputText))
                return "";

            byte[] inputBytes = Encoding.UTF8.GetBytes(inputText + "\0");
            IntPtr inputTextPtr = Marshal.AllocHGlobal(inputBytes.Length);
            Marshal.Copy(inputBytes, 0, inputTextPtr, inputBytes.Length);

            IntPtr resultPtr = _processTextFfi(configPath, inputTextPtr, dictionaryPath, mode, printAll, wakati);
            string result = Marshal.PtrToStringUTF8(resultPtr) ?? string.Empty;

            _freeString(resultPtr);

            Marshal.FreeHGlobal(inputTextPtr);

            return result;
        }
    }
}