using System;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;

namespace CombatSolver;

/// <summary>
/// 0.113.0 把 <c>CardSelectCmd.Selector</c>（静态属性）删了，选牌改为
/// <see cref="MegaCrit.Sts2.Core.CardSelection.CardSelectorStack"/>，实例从
/// <see cref="RunManager.CardSelectorStack"/> 拿。
///
/// 这里做一层薄封装，让调用点只关心「现在有没有选择器」「注入/撤销一个选择器」，
/// 不直接散落 RunManager 取实例的判空逻辑。
/// </summary>
internal static class GameCardSelector
{
    /// <summary>当前的本地选择器；没有（或跑局没开始）时为 null。</summary>
    public static ICardSelector? Current
    {
        get
        {
            try
            {
                return RunManager.Instance?.CardSelectorStack?.LocalSelector;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>是否有选择器挂着（等价于原来 <c>CardSelectCmd.Selector != null</c>）。</summary>
    public static bool IsActive => Current != null;

    /// <summary>
    /// 注入本地选择器，返回的作用域 Dispose 时撤销（等价于原来的
    /// <c>CardSelectCmd.UseSelector</c>）。拿不到跑局时返回 null。
    /// </summary>
    public static IDisposable? Use(ICardSelector selector)
    {
        try
        {
            return RunManager.Instance?.CardSelectorStack?.UseLocalSelector(selector);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>测试用的全局选择器覆盖（等价于原来的 <c>CardSelectCmd.PushSelector</c>）。</summary>
    public static IDisposable? PushTest(ICardSelector selector)
    {
        try
        {
            return MegaCrit.Sts2.Core.CardSelection.CardSelectorStack.SetTestSelector(selector);
        }
        catch
        {
            return null;
        }
    }
}
