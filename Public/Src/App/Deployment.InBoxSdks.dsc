// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

const sdkRoot = Context.getMount("SdkRoot").path;

@@public
export function inBoxSdks(minimalDev: boolean) : Deployment.Definition {
    return createSdkDeploymentDefinition(false, minimalDev, false);
}

@@public
export const inBoxServerSdks = createSdkDeploymentDefinition(true, false, false);

/**
 * Basic sdks without the binary tools, for evaluation purposes only
 */
@@public
export const evaluationOnlySdks = createSdkDeploymentDefinition(false, false, true);

function createSdkDeploymentDefinition(serverDeployment: boolean, minimalDeployment: boolean, evaluationOnly: boolean) : Deployment.Definition {
    return {
        contents: [
            {
                // CODESYNC: Public\Src\FrontEnd\Core\FrontEndHostController.cs (TryCreateInBoxSDKResolver)
                subfolder: "Sdk",
                contents: [
                    {
                        subfolder: "Sdk.Prelude",
                        contents: glob(d`${sdkRoot}/Prelude`, "*.dsc")
                    },
                    {
                        subfolder: "Sdk.Transformers",
                        contents: glob(d`${sdkRoot}/Transformers`, "*.dsc")
                    },
                    // If any new deployment is added below, please consider adding an 'evaluation only' version of it. This
                    // consists of just the specs, but not binaries. The evaluation only SDK is used to deploy SDKs alongside
                    // the VSCode plugin. The plugin only needs specs to evaluate, and adding binaries will make the vsix 
                    // unnecessarily heavy.
                    ...addIfLazy(!serverDeployment && qualifier.targetRuntime === "win-x64", () => [
                        {
                            subfolder: "Sdk.QTest",
                            contents: [ 
                                importFrom("BuildXL.Tools.QTest").selectDeployment(evaluationOnly)
                            ]
                        },
                        // Daemon tools are not included in the minimal deployment
                        ...addIf(!minimalDeployment, 
                            {
                                subfolder: "Sdk.Drop",
                                contents: [ 
                                    importFrom("BuildXL.Tools.DropDaemon").withQualifier({
                                        targetFramework: "net6.0",
                                        targetRuntime: "win-x64"
                                    }).selectDeployment(evaluationOnly)
                                ]
                            },
                            {
                                subfolder: "Sdk.Symbols",
                                contents: [
                                        importFrom("BuildXL.Tools.SymbolDaemon").withQualifier({
                                            targetFramework: "net6.0",
                                            targetRuntime: "win-x64"
                                        }).selectDeployment(evaluationOnly)
                                ]
                            },
                            {
                                subfolder: "Sdk.Materialization",
                                contents: [
                                    importFrom("BuildXL.Tools.MaterializationDaemon").withQualifier({
                                        targetFramework: "net6.0",
                                        targetRuntime: "win-x64"
                                    }).selectDeployment(evaluationOnly)
                                ]
                            }
                        ),
                        {
                            subfolder: "Sdk.JavaScript",
                            contents: [ 
                                importFrom("Sdk.JavaScript").deployment
                            ]
                        },
                        {
                            subfolder: "Sdk.Json",
                            contents: [ 
                                importFrom("Sdk.Json").deployment
                            ]
                        },
                        {
                            subfolder: "Sdk.Guardian",
                            contents: [ 
                                importFrom("BuildXL.Tools.Guardian").deployment
                            ]
                        },
                    ])
                ]
            }
        ]
    };
}
