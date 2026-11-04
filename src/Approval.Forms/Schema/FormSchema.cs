using System.Text.Json;

namespace Approval.Forms.Schema;

/// <summary>表单结构：字段按顺序渲染，提交时服务端按此校验并规范化数据。</summary>
public sealed class FormSchema
{
    public List<FormField> Fields { get; set; } = [];
}

public enum FormFieldType
{
    Text,
    TextArea,
    Number,

    /// <summary>金额，默认保留 2 位小数。</summary>
    Money,

    /// <summary>日期，值为 <c>yyyy-MM-dd</c>。</summary>
    Date,

    /// <summary>日期区间，值为 <c>["yyyy-MM-dd", "yyyy-MM-dd"]</c>。</summary>
    DateRange,

    Select,
    MultiSelect,
    Radio,

    /// <summary>开关，值为 bool；必填表示必须打开（如“我已阅读并同意”）。</summary>
    Switch,

    /// <summary>单个用户，值为用户 ID，可作为审批人（审批人规则 FormField）。</summary>
    User,

    /// <summary>多个用户，值为用户 ID 数组。</summary>
    Users,

    /// <summary>明细表，值为对象数组；列由 <see cref="FormField.Columns"/> 定义。</summary>
    Table,

    /// <summary>说明文字（<see cref="FormField.Label"/> 为标题、<see cref="FormField.Help"/> 为正文），没有值。</summary>
    Description,

    /// <summary>计算字段：值由 <see cref="FormField.Formula"/> 在服务端计算（忽略提交的值），可作为明细列按行计算。</summary>
    Formula,

    /// <summary>附件，值为 <c>[{ id, name, size, contentType }]</c>；文件先通过上传接口保存，提交时按上传记录校验并以服务端元数据为准。</summary>
    File
}

public sealed class FormField
{
    /// <summary>字段标识（字母、数字、下划线），用于条件表达式与模板，如 <c>days &gt; 3</c>、<c>{form.days}</c>。</summary>
    public string Key { get; set; } = "";

    public string Label { get; set; } = "";

    public FormFieldType Type { get; set; }

    public bool Required { get; set; }

    public string? Placeholder { get; set; }

    public string? Help { get; set; }

    /// <summary>默认值，类型与字段值一致。</summary>
    public JsonElement? DefaultValue { get; set; }

    /// <summary>栅格宽度（共 24），默认整行。</summary>
    public int Span { get; set; } = 24;

    /// <summary>Select / MultiSelect / Radio 的选项。</summary>
    public List<FormFieldOption> Options { get; set; } = [];

    /// <summary>数值字段的最小 / 最大值。</summary>
    public decimal? Min { get; set; }

    public decimal? Max { get; set; }

    /// <summary>数值字段的小数位数；Money、Formula 默认 2，Number 默认不限制。</summary>
    public int? Precision { get; set; }

    /// <summary>
    /// 计算字段的公式（见 <see cref="Forms.Formula"/>），如 <c>items_amount_sum - ifnull(advance, 0)</c>；
    /// 明细列的公式按行计算，只能引用本行的数值列，如 <c>qty * price</c>。
    /// </summary>
    public string? Formula { get; set; }

    /// <summary>文本最大长度；默认单行 200、多行 2000，上限 4000。</summary>
    public int? MaxLength { get; set; }

    /// <summary>附件字段最多文件数，默认 10，最大 50。</summary>
    public int? MaxFiles { get; set; }

    /// <summary>附件字段允许的扩展名（不含点），为空时为全局允许的扩展名。</summary>
    public List<string> Accept { get; set; } = [];

    /// <summary>明细表最多行数，默认 100。</summary>
    public int? MaxRows { get; set; }

    /// <summary>明细表的列（仅支持 Text / Number / Money / Date / Select / Formula）。</summary>
    public List<FormField> Columns { get; set; } = [];

    /// <summary>明细表数值列：合计写入顶层字段 <c>{表格}_{列}_sum</c>，可用于条件与模板。</summary>
    public bool Sum { get; set; }

    public static string SumKey(string tableKey, string columnKey) => $"{tableKey}_{columnKey}_sum";

    /// <summary>数值类字段（可参与合计与公式）。</summary>
    public static bool IsNumeric(FormFieldType type) => type is FormFieldType.Number or FormFieldType.Money or FormFieldType.Formula;
}

public sealed record FormFieldOption(string Label, string Value);
