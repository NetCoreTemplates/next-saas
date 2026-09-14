namespace MyApp;

public static class Css
{
    public const string H1 = "text-3xl font-semibold tracking-[-.04em] text-slate-950 dark:text-white sm:text-4xl";
    public const string H2 = "text-2xl font-semibold tracking-[-.035em] text-slate-950 dark:text-white";
    public const string H3 = "mb-4 text-2xl font-semibold tracking-[-.03em] text-slate-950 dark:text-white leading-tight";
    public const string H4 = "text-lg font-semibold text-slate-950 dark:text-white leading-tight";
    public const string Link = "font-semibold text-[#0b5cff] dark:text-blue-300 hover:text-blue-700 dark:hover:text-blue-200";
    public const string LinkUnderline = "underline hover:text-success duration-200 transition-colors";
    public const string PrimaryButton = "cursor-pointer inline-flex min-h-10 items-center justify-center rounded-[10px] border border-transparent py-2.5 px-4 text-sm font-semibold shadow-[0_8px_20px_rgba(11,92,255,.2)] focus:outline-none focus:ring-2 focus:ring-offset-2 dark:ring-offset-slate-950 text-white bg-[#0b5cff] hover:bg-[#084dcc] focus:ring-blue-500";
    public const string SecondaryButton = "cursor-pointer inline-flex min-h-10 items-center justify-center rounded-[10px] border py-2.5 px-4 text-sm font-semibold shadow-sm focus:outline-none focus:ring-2 focus:ring-offset-2 bg-white dark:bg-white/5 border-slate-300 dark:border-white/15 text-slate-700 dark:text-slate-200 hover:bg-slate-50 dark:hover:bg-white/10 focus:ring-blue-500 dark:ring-offset-slate-950";
    public const string DangerButton = "cursor-pointer inline-flex min-h-10 items-center justify-center rounded-[10px] border border-transparent py-2.5 px-4 text-sm font-semibold shadow-sm focus:outline-none focus:ring-2 focus:ring-offset-2 dark:ring-offset-slate-950 focus:ring-red-300 text-white bg-red-600 hover:bg-red-700 focus:ring-red-500";
    public const string LabelClasses = "block text-sm font-semibold text-slate-700 dark:text-slate-300";
    public const string InputText = "block w-full rounded-[10px] border-slate-300 bg-white px-3.5 py-2.5 text-sm text-slate-950 shadow-sm transition placeholder:text-slate-400 focus:border-[#0b5cff] focus:ring-[#0b5cff]/20 disabled:bg-slate-100 disabled:text-slate-500 dark:border-white/15 dark:bg-[#0c1729] dark:text-white dark:disabled:bg-white/5";
    public const string InputCheckbox = "focus:ring-blue-500 h-4 w-4 text-[#0b5cff] rounded border-slate-300 dark:border-white/20 dark:bg-slate-900 dark:ring-offset-slate-950";
    public const string AlertDanger = "mb-2 p-2 px-4 flex items-center rounded text-danger bg-red-100 font-semibold";
    public const string AlertSuccess = "mb-2 p-2 px-4 flex items-center rounded text-success bg-green-100 font-semibold";
    public static string ClassNames(params string?[] classes) => CssUtils.ClassNames(classes);
}
