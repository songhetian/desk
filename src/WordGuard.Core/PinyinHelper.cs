using System.Collections.Generic;
using System.Text;

namespace WordGuard.Core;

/// <summary>
/// 简易中文→拼音转换（只覆盖常用客服违禁词相关的汉字）。
/// 用于键盘钩子兜底模式：输入拼音时也能命中中文违禁词。
/// 不追求全量覆盖，只覆盖常见高风险词用字。
/// </summary>
public static class PinyinHelper
{
    // 常用违禁词用字的拼音映射（按拼音首字母分组）
    // 格式：汉字 -> 拼音（不带声调）
    private static readonly Dictionary<char, string> _map = new()
    {
        // 夸大宣传
        {'最', "zui"}, {'好', "hao"}, {'第', "di"}, {'一', "yi"}, {'百', "bai"}, {'分', "fen"},
        {'低', "di"}, {'价', "jia"}, {'全', "quan"}, {'国', "guo"}, {'首', "shou"}, {'选', "xuan"},
        {'绝', "jue"}, {'对', "dui"}, {'极', "ji"}, {'致', "zhi"}, {'端', "duan"},
        {'顶', "ding"}, {'级', "ji"}, {'尖', "jian"}, {'皇', "huang"}, {'冠', "guan"}, {'王', "wang"},
        {'牌', "pai"}, {'金', "jin"}, {'标', "biao"}, {'杆', "gan"}, {'巨', "ju"}, {'大', "da"},
        {'超', "chao"}, {'凡', "fan"}, {'神', "shen"}, {'奇', "qi"}, {'特', "te"}, {'效', "xiao"},
        {'速', "su"}, {'果', "guo"}, {'功', "gong"}, {'能', "neng"}, {'强', "qiang"},
        {'劲', "jin"}, {'爆', "bao"}, {'款', "kuan"}, {'热', "re"}, {'销', "xiao"}, {'疯', "feng"},
        {'抢', "qiang"}, {'狂', "kuang"}, {'秒', "miao"}, {'杀', "sha"},
        // 诱导承诺
        {'保', "bao"}, {'证', "zheng"}, {'包', "bao"}, {'过', "guo"}, {'必', "bi"}, {'定', "ding"},
        {'肯', "ken"}, {'稳', "wen"}, {'赚', "zhuan"}, {'赢', "ying"}, {'利', "li"}, {'润', "run"},
        {'收', "shou"}, {'益', "yi"}, {'回', "hui"}, {'报', "bao"}, {'返', "fan"}, {'现', "xian"},
        {'奖', "jiang"}, {'品', "pin"}, {'福', "fu"}, {'红', "hong"},
        {'免', "mian"}, {'费', "fei"}, {'送', "song"}, {'赠', "zeng"}, {'领', "ling"}, {'取', "qu"},
        // 价格违规
        {'便', "pian"}, {'宜', "yi"}, {'平', "ping"},
        {'底', "di"}, {'抄', "chao"}, {'跌', "die"}, {'破', "po"},
        {'产', "chan"}, {'地', "di"}, {'原', "yuan"}, {'厂', "chang"}, {'直', "zhi"},
        // 违禁词（通用）
        {'违', "wei"}, {'禁', "jin"}, {'规', "gui"}, {'犯', "fan"}, {'法', "fa"},
        {'骗', "pian"}, {'诈', "zha"}, {'欺', "qi"}, {'假', "jia"}, {'冒', "mao"},
        {'伪', "wei"}, {'劣', "lie"}, {'盗', "dao"}, {'版', "ban"}, {'侵', "qin"}, {'权', "quan"},
        {'泄', "xie"}, {'露', "lu"}, {'秘', "mi"}, {'密', "mi"}, {'隐', "yin"}, {'私', "si"},
        // 其他常见高风险字
        {'限', "xian"}, {'量', "liang"}, {'仅', "jin"}, {'有', "you"},
        {'唯', "wei"}, {'独', "du"}, {'家', "jia"}, {'专', "zhuan"}, {'属', "shu"},
        {'官', "guan"}, {'方', "fang"}, {'正', "zheng"}, {'真', "zhen"}, {'实', "shi"},
        {'天', "tian"}, {'然', "ran"}, {'纯', "chun"}, {'无', "wu"},
        {'添', "tian"}, {'加', "jia"}, {'化', "hua"}, {'学', "xue"}, {'成', "cheng"},
        {'药', "yao"}, {'治', "zhi"}, {'疗', "liao"}, {'病', "bing"}, {'癌', "ai"}, {'症', "zheng"},
        {'痛', "tong"}, {'炎', "yan"}, {'消', "xiao"}, {'止', "zhi"},
        // 数字（中文）
        {'零', "ling"}, {'二', "er"}, {'三', "san"}, {'四', "si"}, {'五', "wu"},
        {'六', "liu"}, {'七', "qi"}, {'八', "ba"}, {'九', "jiu"}, {'十', "shi"},
        {'千', "qian"}, {'万', "wan"}, {'亿', "yi"},
    };

    /// <summary>把中文转成拼音（未识别的字符原样保留）。</summary>
    public static string ToPinyin(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var sb = new StringBuilder(text.Length * 2);
        foreach (var c in text)
        {
            if (_map.TryGetValue(c, out var py))
                sb.Append(py);
            else
                sb.Append(c); // 非汉字（字母/数字/符号）原样保留
        }
        return sb.ToString();
    }

    /// <summary>判断字符串是否看起来是拼音（基本全是小写字母）。</summary>
    public static bool LooksLikePinyin(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        int letterCount = 0;
        foreach (var c in text)
        {
            if (c >= 'a' && c <= 'z') letterCount++;
            else if (c >= 'A' && c <= 'Z') letterCount++;
            else if (char.IsDigit(c)) { /* 数字也可以出现在拼音输入中 */ }
            else return false; // 遇到非字母数字（比如中文），就不是纯拼音
        }
        // 至少有 2 个字母才算拼音输入（避免单个字母误判）
        return letterCount >= 2;
    }
}
