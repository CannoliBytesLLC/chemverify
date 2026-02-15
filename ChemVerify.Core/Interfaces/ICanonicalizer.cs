namespace ChemVerify.Core.Interfaces;

public interface ICanonicalizer
{
    string Canonicalize(string input);
    string CanonicalizeJson(object value);
}

