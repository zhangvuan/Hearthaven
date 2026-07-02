using CommunityToolkit.Mvvm.ComponentModel;
using Hearthaven.Core.Settings;

namespace Hearthaven.ViewModels;

/// <summary>
/// 模型列表条目 — 设置界面中单个模型的显示和编辑。
/// 可从 <see cref="ModelConfig"/> 加载，也可保存回 <see cref="ModelConfig"/>。
/// </summary>
public partial class ModelItem : ObservableObject
{
    /// <summary>显示名称（用户友好名称，如"我的主力模型"）</summary>
    [ObservableProperty]
    private string _displayName = "";

    /// <summary>服务商类型</summary>
    [ObservableProperty]
    private string _provider = "OpenAI Compatible";

    /// <summary>API 地址</summary>
    [ObservableProperty]
    private string _endpoint = "";

    /// <summary>API Key</summary>
    [ObservableProperty]
    private string _apiKey = "";

    /// <summary>模型标识（API 调用时使用的名称，如 deepseek-v4-flash）</summary>
    [ObservableProperty]
    private string _modelId = "";

    /// <summary>最大上下文 Token（可选覆盖全局设置）</summary>
    [ObservableProperty]
    private int? _maxContextTokens;

    /// <summary>最大输出 Token（可选覆盖全局设置）</summary>
    [ObservableProperty]
    private int? _maxTokens;

    /// <summary>是否默认模型</summary>
    [ObservableProperty]
    private bool _isDefault;

    /// <summary>从 ModelConfig 加载</summary>
    public static ModelItem FromConfig(ModelConfig config, bool isDefault)
    {
        return new ModelItem
        {
            DisplayName = config.DisplayName ?? config.Name,
            Provider = config.Provider,
            Endpoint = config.Endpoint ?? "",
            ApiKey = config.ApiKey ?? "",
            ModelId = config.Name,
            MaxContextTokens = config.MaxContextTokens,
            MaxTokens = config.MaxTokens,
            IsDefault = isDefault
        };
    }

    /// <summary>保存回 ModelConfig</summary>
    public ModelConfig ToConfig()
    {
        return new ModelConfig
        {
            Name = ModelId,
            DisplayName = DisplayName == ModelId ? null : DisplayName,
            Provider = Provider,
            Endpoint = string.IsNullOrEmpty(Endpoint) ? null : Endpoint,
            ApiKey = string.IsNullOrEmpty(ApiKey) ? null : ApiKey,
            MaxContextTokens = MaxContextTokens,
            MaxTokens = MaxTokens
        };
    }

    /// <summary>克隆当前实例</summary>
    public ModelItem Clone()
    {
        return new ModelItem
        {
            DisplayName = DisplayName,
            Provider = Provider,
            Endpoint = Endpoint,
            ApiKey = ApiKey,
            ModelId = ModelId,
            MaxContextTokens = MaxContextTokens,
            MaxTokens = MaxTokens,
            IsDefault = IsDefault
        };
    }
}
