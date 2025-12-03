## [0.6.0] - 2025-12-03

### 🚀 Features

- Detect and warn when using auto-generated DTO classes (#202)
- [**breaking**] Change NestedDtoUseHashNamespace behavior (default is true) (#203)
- [**breaking**] Update generated DTO namespaces to use Linqraft prefix instead of hash suffix (#205)
- Add transparent background to scrollbar corner in tailwind.css
- Enhance TAILWIND_CDN_FRAGMENT with additional styles and fonts in DevTailwindUtil.razor

### 🐛 Bug Fixes

- Simplify name conversion and improve consistency in GroupBy usage (#204)
- Update BenchmarkDotNet version and refine benchmark results in README.md

### 🚜 Refactor

- Simplify CodeGenerationService constructor and improve internal attribute filtering

### 📚 Documentation

- Add known issues section for GroupBy and SelectExpr functionality
## [0.5.0] - 2025-12-02

### 🚀 Features

- Add workflow to verify NuGet package installation and functionality (#29)
- Allow local variable capture in SelectExpr via anonymous object parameter (#49)
- Add OrderController and SampleData for API example, update README with usage details
- Add GitHub Actions workflow for code formatting with csharpier
- Add new issue templates for bug reports and feature requests
- Add LQRE001 analyzer for complete external reference capture in SelectExpr (#71)
- Enhance LQRE001 CodeFix to capture instance and static property references intelligently (#79)
- Add LQRF002 analyzer to suggest [ProducesResponseType] for ApiController methods using SelectExpr<T,TDto> (#75)
- Generate DTOs in global namespace when source class is in global namespace (#91)
- Add LQRS002/LQRS003 analyzers for migrating IQueryable.Select to SelectExpr (#93)
- Simplify ternary null checks to `?.` in LQRS002/LQRS003 codefixes (#95)
- Add tests for handling comments in LINQ expressions (#112)
- Enhance DTO generation for nested anonymous types and improve test coverage (#114)
- Add LQRS005 analyzer to detect and remove unnecessary captures (#122)
- Add UnnecessaryCaptureAnalyzer to detect and suggest removal of unused capture variables in SelectExpr
- Enhance SelectExpr to support automatic DTO generation with null-propagation and additional customer info
- Update README to clarify analyzer functionality and add sample animation
- Refactor sample data and classes for improved clarity and structure
- Improve formatting of generated source code for nested structures (#145)
- Add build-info to generated code (#153)
- Generate non-nullable collection types when using Enumerable.Empty fallback (#154)
- Add comments to generated DTOs (#163)
- Implement C# syntax highlighting and update layout styles across components
- Enhance C# syntax highlighting to include property and field declarations
- Update KeyFeaturesSection layout and styles for improved readability and consistency
- Enhance C# syntax highlighting to differentiate between type and property declarations
- Implement collapsible editor pane and sidebar for improved UI/UX
- Add links to web page and online playground in README; improve layout in EditorPane and PreviewPane
- Simplify Playground layout by removing unused CSS classes and improving responsiveness
- Add Min Repro template and URL sharing for issue reporting (#174)
- Update Sidebar UI and enhance Tailwind CSS styles for improved aesthetics
- Add OverloadPriorityAttribute if use .NET 9 or later

### 🐛 Bug Fixes

- Update descendant node retrieval to include the current node in HasNullableAccess method
- Enable locked mode for dotnet restore in workflow files
- Add missing comma and restore additionalVersions in devcontainer.json
- Add missing PropertyGroup for DevelopmentDependency in Linqraft.csproj
- Remove PolySharp package reference from Directory.Build.props
- Add PolySharp package reference and update LangVersion instructions in README files
- Update generic type parameters in SelectExpr methods for consistency
- Update build instructions to ensure a clean build without incremental compilation
- Add clean-test.sh script for easier test cleanup and build process
- Update SelectExpr method signatures for consistency across implementations
- Change Issue33_SealedPatternTest class to partial for extensibility
- Refactor DummyExpression class and update SelectExpr method summaries for clarity (#38)
- Remove unnecessary using Linqraft; statements from README files for clarity
- Include Linqraft.Core.dll in NuGet package (#44)
- Add bug report templates for improved issue reporting
- Update placeholder formatting in bug report template for consistency
- Update bug report template to clarify generated output description
- Update release workflow to improve package handling and artifact management
- Update property accessor pattern in README files for clarity
- Update release workflow to use output from package build for versioning
- Add nullable access operator for LoginPassword properties in ByteArrayCaseTest
- Suppress nullable reference warnings for GenerateFileHeaderPart method
- Update release workflow to use direct output from nbgv for versioning
- Move props file
- Remove Japanese README file and update English README
- Ensure IsPackable property is set in the project file
- Export version to environment and update release steps to use the exported version
- Correct version output variable names in release workflow
- Type generation for `.Select().FirstOrDefault()` with anonymous types (#56)
- Handle direct nested anonymous types in DTO generation (#60)
- Update README to clarify LINQ provider compatibility and enhance generated DTO visibility
- Update workflow name to reflect code formatting purpose
- Update feature request template to correctly mark code fix option as not required
- Update feature request template for clarity and consistency in severity and code fix options
- Remove commented extension from recommendations in VSCode settings
- Add missing comma in VSCode recommendations
- Update format workflow to simplify commit and push logic
- Add SuppressTfmSupportBuildWarnings property to project file
- Update SelectExpr to use inferred type in GetOrdersAnonymousAsync
- Update links in README.md for analyzer documentation
- Clarify parameter name in local variable capture example
- Correctly handle null-conditional operators with chained LINQ methods (#150)
- Downgrade .NET version to 9.0 in playground
- Update links to use relative paths in FaqSection, HeroSection, and MainLayout
- Update property names in order DTO generation for clarity
- Update Tailwind CDN usage to conditional rendering based on build configuration
- Update PackageProjectUrl to point to the correct GitHub Pages URL
- Add permissions for NuGet login and update package publishing command
- Update package description to use null-propagation operators

### 🚜 Refactor

- Streamline NuGet package verification workflow by consolidating directory creation and file copying steps
- Remove unnecessary TargetFrameworks from project files
- Remove Windows from build matrix due to performance issues
- Rename DummyExpression to SelectExprExtensions and improve error handling
- Streamline SelectExpr method implementations and improve exception handling
- Remove TryTutorialCaseAnonymous test method to streamline tutorial cases
- Simplify OrderController GetOrdersAsync method by removing unnecessary properties in DTO mapping
- Update SelectExpr usage in README to clarify DTO mapping
- Update performance metrics in README for DTO generation
- Simplify AnalyzeAnonymousType call by removing unused property accessibilities (#62)
- Remove OverloadResolutionPriority attribute from SelectExpr methods
- Clean up item selection syntax for improved readability
- Simplify output verification step in workflow
- Reorganize Dependabot workflow to include package testing steps
- Streamline Dependabot workflow to target .NET 10.0 and simplify .NET setup
- Improve layout and styling in HeroSection, EditorPane, Sidebar, and Playground components; update package references
- Rename DTO classes for clarity and improve null handling in queries

### 📚 Documentation

- Update C# version requirements and remove Polysharp references in README files
- Update C# version requirements and add details on language features in README files
- Add note about installing Linqraft in multiple projects
- Fix capitalization in prerequisites section of README
- Update README to clarify usage of generated DTOs and provide examples
- Enhance README to clarify the benefits of integrating DTOs with query logic
- Enhance README to clarify the benefits of integrating DTOs with query logic and address concerns about DTO separation
- Analyzer document path fix
- Update analyzer documentation for clarity and consistency
- Clarify suggested transformations in LQRS004 analyzer documentation
- Enhance LQRS003 analyzer documentation with detailed code fix examples
- Update usage section to clarify C# 12 feature usage
- Clarify C# 12 requirement and interceptor feature usage in README
- Add overview and prefixes sections to analyzers README
- Remove markdown rendering from bug report and feature request templates
- Enhance README with code fix explanation and clarify refactoring prefix
- Add .editorconfig file to enforce coding styles
- Improve clarity of analyzer description in README
- Update README to clarify Linqraft's functionality and usage
- Remove sample animation placeholder from README
- Add section explaining the advantages of using Linqraft for efficient data fetching
- Update README to clarify features and remove unnecessary dependencies
- Clarify nullability removal for array types in README
- Update README to clarify prerequisites for .NET SDK and Visual Studio versions

### 🎨 Styling

- Enhance typography and spacing across components
## [0.3.0] - 2025-11-16

### 🚀 Features

- Add library icon (#21)
- Enhance DtoProperty analysis with nullable checks from target properties
- Add usage note for TypeScript ORM Prisma in README
- Improve wording for Prisma usage in README files
- Enhance DtoProperty analysis to support named types and improve null checks in nested selects
- Add benchmark for Linqraft Manual DTO selection
- Add pragma warning disable for CS8602 in README files
- Add pragma warning disable for CS8602 in SelectExprGroups
- Update README files and restructure documentation for clarity and organization
- Add Linqraft.ApiSample project with initial setup and sample API implementation
- Add GetDtoNamespace method to SelectExprInfo classes for improved namespace handling
- Add bug report issue template for improved issue tracking
- Add Linqraft.MinimumSample project with sample data and execution logic
- Add Linqraft.MinimumSample.OldVersion project with sample data and execution logic
- Enhance DTO generation with nested class support and internal accessibility

### 🐛 Bug Fixes

- Remove test step from release workflow
- Update image path in Japanese README for correct asset reference
- Correct formatting and labels in bug report issue template
- Update C# version requirements from 12 to 13 in README files
- Add CheckEolTargetFramework property to OldVersion project file
- Change default accessibility from internal to public in GetAccessibilityString method
- Correct TargetFrameworks to TargetFramework in Directory.Build.props

### 🚜 Refactor

- Clean up code formatting and improve readability in various files

### 📚 Documentation

- Update README to clarify auto-generation of Items type in example
## [0.2.0] - 2025-11-15

### 🚀 Features

- Add lambda parameter name handling in SelectExpr processing
- Add methods to handle IEnumerable select expressions with nullable operators
- Change classes to partial for extensibility in DTOs
- Add note on extending generated DTO classes as partial
- Recreate bug report template with improved structure and descriptions
- Remove feature request template
- Enhance DTO generation with null-conditional handling and add corresponding tests
- Add test for PartialNestedDto generation in the same class
- Mark FullName property as required in EnumerableSimpleDto
- Add DeepWiki badge to README files
- Support .NET standard 2.0 (#20)

### 🐛 Bug Fixes

- Correct indentation in generated expression code
- Generate DTOs in caller namespace instead of source type namespace
- Generate DTOs in nested class structure for predefined partial DTOs
- Update benchmark section titles and improve performance comparison wording in README
- Remove unused 'examples' project from solution
- Update README and test files for improved null handling and DTO initialization
- Enhance nullable access check and add comprehensive tests for list nullability scenarios
- Add GitHubActions logger to test workflow and update test logger package reference
- Update README to enhance examples for anonymous and explicit DTO patterns
- Normalize baseExpression formatting by removing unnecessary whitespace and adjusting property access spacing
- Update README to clarify C# version requirements and .NET compatibility notes
- Update README to clarify setup instructions for .NET 7 or below
- Remove --prerelease flag from NuGet installation command in README

### 📚 Documentation

- Update README to include setup instructions for .NET 7 or below
## [0.1.0] - 2025-11-14

### 🚀 Features

- Add initial test cases for SelectExpr functionality
- Add launchSettings.json for debugging configurations
- Add DummyExpression class with SelectExpr method for EFCore.ExprGenerator
- Enhance DtoProperty and DtoStructure for improved expression analysis and DTO generation
- Update project references and add manual test data for improved test coverage
- Enhance SelectExprInfoAnonymous for better type handling and add comprehensive tests for nested and simple cases
- Add package description to Directory.Build.props for improved clarity
- Enhance NestedCaseTest to include values for GrandChild2 and update assertions for improved test coverage
- Add accessibility modifiers to generated classes in SelectExprInfo and SelectExprInfoAnonymous
- Improve handling of nested Select expressions and support for chained methods in DtoProperty and SelectExprInfo
- Enhance SelectExpr generation with overload resolution priority and inheritance depth calculation
- Enhance SelectExpr to support explicit DTO types and improve code generation

### 🐛 Bug Fixes

- 修正されたDTOクラス生成とSelectExprメソッドの完全修飾名の使用
- Add dto class name to GeneratedExpression
- Add OverloadResolutionPriority attribute to SelectExpr method
- Simplify SelectExpr method signature formatting
- Remove unused project reference and delete obsolete unit test file
- Remove OverloadResolutionPriority attribute from SelectExpr method
- Update CompilerGeneratedFilesOutputPath to be consistent across projects
- Update interceptor namespaces in project files for consistency
- Correct namespace declaration in DummyExpression.cs
- Remove InterceptorsNamespaces from project files and add SourceGenerator.targets for interceptor configuration
- Simplify DTO name handling by removing global:: prefix from nested DTOs
- Add missing ProjectSection for example project in solution file
- Update interceptor namespaces in project files for consistency
- Refactor GenerateDtoClasses method to remove namespace parameter and add namespace handling in derived classes
- Simplify build and test instructions by combining clean and build commands
- Update overload resolution attribute handling in SelectExprInfo classes
- Change dbContext field to readonly in UseEFCoreSenarioTest class
- Handle null case in DTO generation example in README

### 🚜 Refactor

- Use default instead of null
- Remove EFCore.ExprGenerator.Core project and update references
- Update intercepts location attribute syntax in method header generation
- Simplify property access in SelectExpr for SampleClassSimpleDto
- Update collection initialization syntax in test classes for consistency
- Improve nullable access handling and simplify DTO class generation in SelectExprInfo
- Enhance unique ID generation and error handling for SelectExpr processing
- Update initialization syntax for Locations and streamline SelectExpr method generation
- Remove unused accessibility variable and comment out test cases in NestedCaseTest and SameClassManyPatternsTest
- Extract file header generation into a separate method for cleaner code
- Update SelectExpr methods to handle multiple interceptable locations
- Remove redundant default value cases for numeric types in GetDefaultValueForType method
- Simplify DtoStructure by using ITypeSymbol directly and removing redundant properties
- Update DTO generation and SelectExpr methods for improved structure and clarity
- Adjust indentation in SelectExpr methods for consistency and clarity
- Update comments in ConvertNullableAccessToExplicitCheck for clarity on nullable access conversion
- Improve nullable type handling and DTO class naming in SelectExpr methods
- Reorganize DummyExpression class structure and improve method documentation
- Enhance nested DTO handling and improve code structure in GenerateDtoClassInfo and SelectExprInfoExplicitDto
- Remove unnecessary file deletion in Dispose method of UseEFCoreSenarioTest
- Comment out unused properties in SelectExpr for NestedCase and TutorialCase tests
- Enhance nullable handling and improve Select expression processing in Dto generation
- Update class declaration to be partial and improve nested class name formatting
- Enhance TargetNamespace handling and add GetActualDtoNamespace method for consistency
- Remove unused accessibility variable in GenerateSelectExprMethod
- Simplify conditional check for select invocation in AnalyzeExpression method
- Update SelectExpr usage to specify DTO types and enhance syntax handling

### 📚 Documentation

- Add Japanese translation for README
- Add example of generated method in README
- Remove outdated example from README
- Add license section to README
- Add developer guide for EFCore.ExprGenerator
- Add authors section to Directory.Build.props
- Add instructions to enable interceptor in csproj
- Update README to clarify DTO auto-generation and nullable expression support
- Add example of existing DTO class in usage section
- Add prerequisites section to README for .NET 8.0 requirement and update usage examples in tests
- Update README files to enhance clarity and add examples for auto-generated DTOs and null-propagation support
- Update installation instructions in README to specify bash code block
- Update README to include example of auto-generated DTOs with null-propagation support
- Remove outdated interceptor configuration instructions from README
- Add quality gate and maintainability badges to README
- Add troubleshooting section to README for CS8072 error resolution
- Clarify usage of auto-generated type information in examples
- Remove note about EFCore dependency from README
- Update comments for clarity and consistency in README and DummyExpression

### 🎨 Styling

- Improve code formatting for better readability in SelectExprInfo.cs

### 🧪 Testing

- Add type assertions for converted DTOs in SimpleCase tests
- Update type assertions in SimpleCaseTest to check for type name patterns
- Add unit tests for SameClassManyPatterns with various SelectExpr scenarios
- Add UseEFCoreSenarioTest to validate SelectExpr functionality with sample data
- Comment out unused test cases and clean up data structures in SimpleCaseTest
- Add AnonymousCaseTest to validate SelectExpr functionality with various name formats
- Add additional test cases for SelectExpr functionality and improve database file handling
- Add RootNamespaceTest to validate global namespace handling
