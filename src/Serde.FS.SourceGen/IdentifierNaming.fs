namespace Serde.FS

/// Backtick-quoting of F# identifiers for code generation.
///
/// Emitters synthesize identifiers from user type/field names. When a generated
/// name collides with an F# reserved keyword (e.g. a field `Namespace` lowercased
/// to `namespace`, or a field literally named ``type``) the emitted code fails to
/// compile. This module wraps such names in double-backticks.
///
/// It deliberately hardcodes the keyword set rather than depending on
/// FSharp.Compiler.Service / Fantomas.FCS: the source-generator projects reference
/// only the lightweight TypeModel, and the keyword list is small and stable. The
/// module is self-contained so it can be lifted into other tooling.
module IdentifierNaming =

    /// F# reserved keywords plus words reserved for future use, all of which
    /// require backticks to be used as identifiers. Mirrors the set recognised by
    /// FSharp.Compiler's PrettyNaming.
    let keywords : Set<string> =
        set [
            // Active keywords
            "abstract"; "and"; "as"; "assert"; "base"; "begin"; "class"; "default"
            "delegate"; "do"; "done"; "downcast"; "downto"; "elif"; "else"; "end"
            "exception"; "extern"; "false"; "finally"; "fixed"; "for"; "fun"
            "function"; "global"; "if"; "in"; "inherit"; "inline"; "interface"
            "internal"; "lazy"; "let"; "match"; "member"; "module"; "mutable"
            "namespace"; "new"; "not"; "null"; "of"; "open"; "or"; "override"
            "private"; "public"; "rec"; "return"; "select"; "static"; "struct"
            "then"; "to"; "true"; "try"; "type"; "upcast"; "use"; "val"; "void"
            "when"; "while"; "with"; "yield"
            // Reserved for future use
            "atomic"; "break"; "checked"; "component"; "const"; "constraint"
            "constructor"; "continue"; "eager"; "event"; "external"; "functor"
            "include"; "method"; "mixin"; "object"; "parallel"; "process"
            "protected"; "pure"; "sealed"; "tailcall"; "trait"; "virtual"; "volatile"
        ]

    let private isValidIdentifierChar isFirst (c: char) =
        if isFirst then System.Char.IsLetter c || c = '_'
        else System.Char.IsLetterOrDigit c || c = '_' || c = '\''

    let private isValidIdentifier (s: string) =
        not (System.String.IsNullOrEmpty s)
        && isValidIdentifierChar true s.[0]
        && Seq.forall (isValidIdentifierChar false) (s.Substring 1)

    /// True when `name` must be wrapped in double-backticks to be a legal F#
    /// identifier — i.e. it is a reserved keyword or contains characters that
    /// aren't valid in a bare identifier. Already-backticked names return false.
    let needsBackticks (name: string) =
        not (System.String.IsNullOrEmpty name)
        && not (name.StartsWith "``")
        && (keywords.Contains name || not (isValidIdentifier name))

    /// Wraps `name` in double-backticks when required; otherwise returns it
    /// unchanged. Idempotent — an already-backticked name is returned as-is.
    let backtick (name: string) =
        if needsBackticks name then "``" + name + "``" else name
