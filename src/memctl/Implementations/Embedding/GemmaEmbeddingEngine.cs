using System.Runtime.InteropServices;
using Memctl.CoreAbstractions.Ports;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace Memctl.Implementations.Embedding;

public sealed class GemmaEmbeddingEngine : IEmbeddingEngine
{
    private const string ModelRepo      = "onnx-community/embeddinggemma-300m-ONNX";
    private const string HfBaseUrl      = "https://huggingface.co";
    private const int    MaxSeqLength   = 512;

    private static readonly (string Src, string Dst)[] ModelFiles =
    [
        ("onnx/model_quantized.onnx",     "model_quantized.onnx"),
        ("onnx/model_quantized.onnx_data","model_quantized.onnx_data"),
        ("tokenizer.model",               "tokenizer.model"),
        ("tokenizer_config.json",         "tokenizer_config.json"),
    ];

    private readonly InferenceSession _session;
    private readonly Tokenizer        _tokenizer;
    private readonly string           _embeddingOutput;
    private readonly bool             _needsMeanPool;

    public string ModelName { get; }
    public int    Dim       { get; }

    private GemmaEmbeddingEngine(InferenceSession session, Tokenizer tokenizer, string modelName)
    {
        _session       = session;
        _tokenizer     = tokenizer;
        ModelName      = modelName;

        _embeddingOutput = session.OutputNames.Contains("sentence_embedding")
            ? "sentence_embedding"
            : session.OutputNames[0];

        var meta       = session.OutputMetadata[_embeddingOutput];
        _needsMeanPool = meta.Dimensions.Length == 3;
        Dim            = (int)(meta.Dimensions.Length == 2 ? meta.Dimensions[1] : meta.Dimensions[2]);
    }

    public static bool IsReady(string? modelDir = null)
    {
        var dir = modelDir ?? DefaultModelDir();
        return Directory.Exists(dir) && File.Exists(Path.Combine(dir, "model_quantized.onnx"));
    }

    public static async Task<GemmaEmbeddingEngine> CreateAsync(string? modelDir = null)
    {
        var dir       = modelDir ?? DefaultModelDir();
        var modelName = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, '/'));
        await EnsureDownloadedAsync(dir);

        var modelPath     = Path.Combine(dir, "model_quantized.onnx");
        var tokenizerPath = Path.Combine(dir, "tokenizer.model");

        var session = new InferenceSession(modelPath);
        using var stream  = File.OpenRead(tokenizerPath);
        var tokenizer = LlamaTokenizer.Create(stream, addBeginOfSentence: true, addEndOfSentence: false);

        return new GemmaEmbeddingEngine(session, tokenizer, modelName);
    }

    public float[] Embed(string text)
    {
        var ids = _tokenizer.EncodeToIds(text)
                            .Take(MaxSeqLength)
                            .Select(id => (long)id)
                            .ToArray();

        var mask = Enumerable.Repeat(1L, ids.Length).ToArray();

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids",
                new DenseTensor<long>(ids, [1, ids.Length])),
            NamedOnnxValue.CreateFromTensor("attention_mask",
                new DenseTensor<long>(mask, [1, mask.Length])),
        };

        // Some models expect token_type_ids
        if (_session.InputNames.Contains("token_type_ids"))
            inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids",
                new DenseTensor<long>(new long[ids.Length], [1, ids.Length])));

        using var results = _session.Run(inputs);
        var output = results.First(r => r.Name == _embeddingOutput);
        var tensor = output.AsTensor<float>();

        return _needsMeanPool ? MeanPool(tensor, mask) : Normalize(tensor.ToArray());
    }

    private static float[] MeanPool(Tensor<float> tensor, long[] mask)
    {
        var dim    = tensor.Dimensions[2];
        var result = new float[dim];
        var count  = (int)mask.Sum();

        for (var s = 0; s < mask.Length; s++)
        {
            if (mask[s] == 0) continue;
            for (var d = 0; d < dim; d++)
                result[d] += tensor[0, s, d];
        }

        for (var d = 0; d < dim; d++)
            result[d] /= count;

        return Normalize(result);
    }

    private static float[] Normalize(float[] v)
    {
        var norm = MathF.Sqrt(v.Sum(x => x * x));
        if (norm < 1e-8f) return v;
        for (var i = 0; i < v.Length; i++) v[i] /= norm;
        return v;
    }

    private static async Task EnsureDownloadedAsync(string dir)
    {
        if (Directory.Exists(dir) && File.Exists(Path.Combine(dir, "model_quantized.onnx")))
            return;

        Directory.CreateDirectory(dir);
        Console.Error.WriteLine($"First run: downloading EmbeddingGemma model to {dir}");
        Console.Error.WriteLine("This is a one-time download (~310 MB).");

        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromMinutes(30);

        foreach (var (src, dst) in ModelFiles)
        {
            var dest = Path.Combine(dir, dst);
            if (File.Exists(dest)) continue;

            var url = $"{HfBaseUrl}/{ModelRepo}/resolve/main/{src}";
            Console.Error.Write($"  {dst} ... ");

            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            await using var file   = File.Create(dest);
            await stream.CopyToAsync(file);

            Console.Error.WriteLine("done");
        }
    }

    private static string DefaultModelDir() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".memctl", "models", "embeddinggemma-300m");

    public void Dispose() => _session.Dispose();
}
