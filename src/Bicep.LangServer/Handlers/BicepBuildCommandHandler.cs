// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Deployments.Core.Entities;
using Azure.Deployments.Core.Helpers;
using Bicep.Core.Analyzers.Interfaces;
using Bicep.Core.Analyzers.Linter;
using Bicep.Core.Analyzers.Linter.ApiVersions;
using Bicep.Core.Configuration;
using Bicep.Core.Diagnostics;
using Bicep.Core.Emit;
using Bicep.Core.Features;
using Bicep.Core.FileSystem;
using Bicep.Core.Registry;
using Bicep.Core.Semantics;
using Bicep.Core.Semantics.Namespaces;
using Bicep.Core.Workspaces;
using Bicep.LanguageServer.CompilationManager;
using Bicep.LanguageServer.Utils;
using Microsoft.WindowsAzure.ResourceStack.Common.Json;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;

namespace Bicep.LanguageServer.Handlers
{
    // This handler is used to generate compiled .json file for given a bicep file path.
    // It returns build succeeded/failed message, which can be displayed approriately in IDE output window
    public class BicepBuildCommandHandler : ExecuteTypedResponseCommandHandlerBase<string, string>
    {
        private readonly ICompilationManager compilationManager;
        private readonly IFeatureProviderFactory featureProviderFactory;
        private readonly IFileResolver fileResolver;
        private readonly IModuleDispatcher moduleDispatcher;
        private readonly INamespaceProvider namespaceProvider;
        private readonly IConfigurationManager configurationManager;
        private readonly IApiVersionProviderFactory apiVersionProviderFactory;
        private readonly IBicepAnalyzer bicepAnalyzer;

        public BicepBuildCommandHandler(ICompilationManager compilationManager, ISerializer serializer, IFeatureProviderFactory featureProviderFactory, INamespaceProvider namespaceProvider, IFileResolver fileResolver, IModuleDispatcher moduleDispatcher, IApiVersionProviderFactory apiVersionProviderFactory, IConfigurationManager configurationManager, IBicepAnalyzer bicepAnalyzer)
            : base(LangServerConstants.BuildCommand, serializer)
        {
            this.compilationManager = compilationManager;
            this.featureProviderFactory = featureProviderFactory;
            this.namespaceProvider = namespaceProvider;
            this.fileResolver = fileResolver;
            this.moduleDispatcher = moduleDispatcher;
            this.configurationManager = configurationManager;
            this.apiVersionProviderFactory = apiVersionProviderFactory;
            this.bicepAnalyzer = bicepAnalyzer;
        }

        public override async Task<string> Handle(string bicepFilePath, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(bicepFilePath))
            {
                throw new ArgumentException("Invalid input file path");
            }

            DocumentUri documentUri = DocumentUri.FromFileSystemPath(bicepFilePath);
            string buildOutput = await GenerateCompiledFileAndReturnBuildOutputMessageAsync(bicepFilePath, documentUri);

            return buildOutput;
        }

        private async Task<string> GenerateCompiledFileAndReturnBuildOutputMessageAsync(string bicepFilePath, DocumentUri documentUri)
        {
            string compiledFilePath = PathHelper.GetDefaultBuildOutputPath(bicepFilePath);
            string compiledFile = Path.GetFileName(compiledFilePath);

            // If the template exists and contains bicep generator metadata, we can go ahead and replace the file.
            // If not, we'll fail the build.
            if (File.Exists(compiledFilePath) && !TemplateContainsBicepGeneratorMetadata(File.ReadAllText(compiledFilePath)))
            {
                return "Bicep build failed. The output file \"" + compiledFile + "\" already exists and was not generated by Bicep. If overwriting the file is intended, delete it manually and retry the build command.";
            }

            var fileUri = documentUri.ToUri();

            var compilation = await CompilationHelper.GetCompilationAsync(
                documentUri,
                fileUri,
                apiVersionProviderFactory,
                bicepAnalyzer,
                compilationManager,
                configurationManager,
                featureProviderFactory,
                fileResolver,
                moduleDispatcher,
                namespaceProvider);

            var diagnosticsByFile = compilation.GetAllDiagnosticsByBicepFile()
                .FirstOrDefault(x => x.Key.FileUri == fileUri);

            if (diagnosticsByFile.Value.Any(x => x.Level == DiagnosticLevel.Error))
            {
                return "Bicep build failed. Please fix below errors:\n" + DiagnosticsHelper.GetDiagnosticsMessage(diagnosticsByFile);
            }

            using var fileStream = new FileStream(compiledFilePath, FileMode.Create, FileAccess.ReadWrite);
            var model = compilation.GetEntrypointSemanticModel();
            var emitter = new TemplateEmitter(model);
            EmitResult result = emitter.Emit(fileStream);

            return "Bicep build succeeded. Created ARM template file: " + compiledFile;
        }

        // Returns true if the template contains bicep _generator metadata, false otherwise
        public bool TemplateContainsBicepGeneratorMetadata(string template)
        {
            try
            {
                if (!string.IsNullOrEmpty(template))
                {
                    JToken jtoken = template.FromJson<JToken>();
                    if (TemplateHelpers.TryGetTemplateGeneratorObject(jtoken, out DeploymentTemplateGeneratorMetadata generator))
                    {
                        if (generator.Name == "bicep")
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }

            return false;
        }
    }
}
