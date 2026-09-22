namespace Pisum.Transcribe.Tests;

public static class Traits
{
    public const string Category = "Category";

    public static class Categories
    {
        public const string Unit = "Unit";
        public const string Integration = "Integration";

        /// <summary>
        /// Needs a microphone, a GPU, a downloaded model or internet access. Mark these tests with
        /// <c>[Fact(Explicit = true)]</c>.
        /// </summary>
        public const string Hardware = "Hardware";
    }
}
