# .NET solution guidance

These instructions apply to .NET solutions and projects under this directory. Before
changing code, inspect relevant project documentation, nearby implementation, and
related tests.

## Project structure

- `domore-async-filesystemwatching.slnx` is the solution entry point.
- `source/Domore.Async.FileSystemWatching/` contains the production library for
  async file-system event callbacks.
- `tests/Domore.Async.FileSystemWatching.Tests/` contains the corresponding test
  project.
- `.github/workflows/` contains the repository automation workflows.
- `Directory.Build.props`, `Directory.Packages.props`, and `global.json` define
  shared build, package, and SDK settings.
- `out/` contains generated build and test output; do not edit or commit it.

## Preserve existing work

- When working in a Git repository, inspect its status and existing diffs before
  editing. Preserve existing work, avoid reverting unrelated changes, and keep
  changes scoped to the requested task.
- Do not commit generated build or test output, packages, or IDE state unless the
  task specifically requires them.

## Build and test

- Identify the solution or project files, affected projects, and target frameworks
  before building.
- Use the SDK selected by `global.json` when present, along with the operating
  system, workloads, targeting packs, and runtimes required by the affected
  projects.
- From the solution directory, use the applicable commands:

  ```sh
  dotnet restore
  dotnet build --no-restore --configuration Release
  dotnet test --no-build --no-restore --configuration Release
  dotnet pack --no-build --no-restore --configuration Release
  ```

- Pack only projects that produce packages. For focused work, build or test the
  affected project and target framework before running broader validation.
- For code changes, run relevant tests and build all affected target frameworks.
  Use full solution validation when changing shared build settings, dependencies,
  or behavior that spans projects. For documentation-only changes, review the
  content and diff.
- Use `--no-build` only when the binaries include the latest code changes. Report
  the checks performed and any validation that could not run.

## Code and project conventions

- Use CRLF line endings unless specified otherwise in `.editorconfig`.
- Follow `.editorconfig`, shared build settings, and the conventions used by nearby
  code. Preserve existing project and framework-specific build configuration.
- Use APIs available on every target framework affected by the change. A newer
  language version does not make newer runtime APIs available on older frameworks.
- When central package management is configured, manage dependency versions in its
  central properties file and omit versions from project package references.
- Do not add to the public API unless explicitly requested. Keep new types and
  members internal or private when that permits the requested change.
- Prefer immutable types. When state mutation is required, keep it private. Prefer
  read-only properties supplied through constructors and init-only properties.
  Pure data objects should be `record` types.
- Follow existing type organization. Avoid concrete classes that are intended to
  remain closed to extension; declare them `sealed` when appropriate, or `abstract`
  when they are designed as base types.
- Update public API documentation and relevant usage examples when changing a
  documented contract. Do not add public documentation comments to private or
  internal members.
- Keep changes focused. Avoid unrelated formatting, framework, dependency, or
  release-workflow changes.

### Properties

Prefer compiler-generated backing fields. In C# 14 or later, use the `field`
keyword when a property needs custom accessor logic but can use an automatic
backing field. Keep accessors without custom logic auto-implemented, and do not
declare a separate backing field for these properties. For a custom setter,
validate `value` and assign it to `field` after validation:

```csharp
using System;
using System.Threading;

internal sealed class MyNewClass {
    private readonly
#if NET9_0_OR_GREATER
        Lock
#else
        object
#endif
        Locker = new();

    private void MyPrivateMethod() {
        /*
         * This is the comment style to use. Prefer this style
         * when writing comments.
         */
        lock (Locker) {
            Console.WriteLine("Hello, World!"); // Only use this style of comment
                                                 // when a single line needs clarification.
        }
    }

    internal string MyInternalString {
        get;
        set {
            bool valueIsNull = value is null;
            if (valueIsNull) {
                throw new ArgumentNullException(nameof(MyInternalString));
            }
            field = value;
        }
    } = string.Empty;

    internal void MyInternalMethod() {
    }

    /// <summary>
    /// Gets the string supplied when this instance was created.
    /// </summary>
    public string MyPublicString { get; }

    /// <summary>
    /// Gets or sets a string that is initialized when first read.
    /// </summary>
    public string MyStringWithAutoBackingField {
        get => field ??= "Hello, World!";
        set;
    }

    /// <summary>
    /// Gets or sets a non-negative number. The default value is 100.
    /// </summary>
    public double MyDoubleWithDefault {
        get;
        set {
            if (value < 0) {
                throw new ArgumentOutOfRangeException(
                    nameof(MyDoubleWithDefault),
                    value,
                    "The value must not be negative.");
            }

            field = value;
        }
    } = 100;

    /// <summary>
    /// Initializes a new instance of the <see cref="MyNewClass"/> class.
    /// </summary>
    /// <param name="myPublicString">
    /// The string to expose through <see cref="MyPublicString"/>.
    /// </param>
    public MyNewClass(string myPublicString) {
        MyPublicString = myPublicString ?? throw new ArgumentNullException(nameof(myPublicString));
    }

    /// <summary>
    /// Performs the public operation.
    /// </summary>
    public void MyPublicMethod() {
    }
}
```

For synchronization fields, use `System.Threading.Lock` when the target
framework is `net9.0` or greater. In multi-targeted code, use
`NET9_0_OR_GREATER` to select `Lock` for those targets and `object` for earlier
targets, as shown above.

Use a manually declared field when an automatic backing field would make the
code more complicated, such as when the field must be `volatile`.

### C# style

- Follow the target repository's `.editorconfig` and nearby code. The repositories
  commonly use four spaces for C# indentation and put opening braces on the same
  line; preserve the project's configured line endings and other local settings.
- Use block-bodied methods and constructors with opening and closing braces. Do
  not use expression-bodied methods (`=>`). Expression-bodied properties and
  accessors are fine when they keep the property clear.
- Always use braces around statement bodies, including `if`/`else`, `do`, `for`,
  `foreach`, `while`, `using`, and `lock`, even when the body is one statement.
- Always use a descriptively named `bool` variable as the condition of an `if`.
  Keep logic out of `if` and `while` conditions by calculating it beforehand;
  update the condition variable explicitly inside loops.
- Check reference values for null with `is null` and `is not null`. Prefer these
  patterns over `== null` and `!= null`.
- Prefer `/* ... */` block comments, with ` * ` at the start of each interior
  line. Use `//` only for a single-line clarification; align a continuation line
  with the comment when needed.
- In XML documentation, put each element's opening tag, content, and closing tag
  on separate lines. Document public APIs; do not add XML documentation comments
  to private or internal members.
- Use PascalCase for types and members, and prefix interfaces with `I`. Follow
  the repository's member ordering and `var` preferences. Prefer private
  `readonly` fields where possible, keep fields non-public, and put each type in
  a separate file named after the type.
- Prefer file-scoped namespaces where supported by the project, and declare
  closed concrete classes `sealed`.

## Tests

- Use the test framework, assertion style, and helpers already used by the affected
  projects.
- Add regression coverage for behavior changes. Confirm that a regression test
  fails because of the reported defect, rather than because of a build or
  environment problem.
- Prefer explicit task signals with bounded waits over timing-dependent sleeps.
- Keep tests and shared test helpers compatible with all applicable target
  frameworks.

## Code reviews

- When asked to review code, inspect the full requested scope and record each
  finding with its impact, a suggested resolution, and file and line references
  where applicable. Read any existing review findings first, preserve their
  identifiers, and avoid duplicate findings.
- When findings use stable issue numbers, assign new findings numbers greater than
  the highest number already used. Do not renumber or reuse numbers, including for
  resolved findings.
- When asked to fix review findings, work only on the specified issues. Prefer
  regression tests that demonstrate each issue before fixing it. Keep resolved
  findings in the review record and mark them resolved.
- Before reporting completion, inspect the final diff and any new or untracked
  files for unintended changes, whitespace problems, and accidental public API
 additions.
