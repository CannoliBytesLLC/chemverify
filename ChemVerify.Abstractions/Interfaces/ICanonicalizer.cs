namespace ChemVerify.Abstractions.Interfaces;

public interface ICanonicalizer
{
    string Canonicalize(string input);
    string CanonicalizeJson(object value);
}
