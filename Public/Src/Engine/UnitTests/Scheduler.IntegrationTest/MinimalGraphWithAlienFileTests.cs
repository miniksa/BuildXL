﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Native.IO;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler
{
    public class MinimalGraphWithAlienFileTests : SchedulerIntegrationTestBase
    {
        public MinimalGraphWithAlienFileTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void FingerprintIsStable()
        {
            // Schedule a pip that produces the three kind of available outputs (declared, shared and exclusive opaque outputs)
            // and make sure the fingerprint is stable when there are no changes
            AbsolutePath dirPath = AbsolutePath.Create(Context.PathTable, Path.Combine(SourceRoot, "dir"));
            AbsolutePath sod = dirPath.Combine(Context.PathTable, "sod");
            AbsolutePath eod = dirPath.Combine(Context.PathTable, "eod");
            DirectoryArtifact dirToEnumerate = DirectoryArtifact.CreateWithZeroPartialSealId(dirPath);
            var declaredOuput = CreateOutputFileArtifact(root: dirPath);
            var sharedOpaqueOutput = CreateOutputFileArtifact(root: sod);
            var exclusiveOpaqueOutput = CreateOutputFileArtifact(root: eod);

            var operations = new List<Operation>
            {
                Operation.WriteFile(declaredOuput),
                Operation.WriteFile(sharedOpaqueOutput, doNotInfer: true),
                Operation.WriteFile(exclusiveOpaqueOutput, doNotInfer: true),
                Operation.EnumerateDir(dirToEnumerate, doNotInfer: true),
            };

            var builder = CreatePipBuilder(operations);
            builder.AddOutputDirectory(sod, global::BuildXL.Pips.Operations.SealDirectoryKind.SharedOpaque);
            builder.AddOutputDirectory(eod, global::BuildXL.Pips.Operations.SealDirectoryKind.Opaque);

            // This makes sure we use the right file system, which is aware of alien files
            builder.Options |= global::BuildXL.Pips.Operations.Process.Options.AllowUndeclaredSourceReads;

            var pip = SchedulePipBuilder(builder);

            // Run once
            RunScheduler().AssertSuccess();
            // Run a second time. Nothing changed, we should get a hit
            RunScheduler().AssertCacheHit(pip.Process.PipId);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AlienFilesArePartOfTheFingerprint(bool isUndeclaredRead)
        {
            AbsolutePath dirPath = AbsolutePath.Create(Context.PathTable, Path.Combine(SourceRoot, "dir"));
            DirectoryArtifact dirToEnumerate = DirectoryArtifact.CreateWithZeroPartialSealId(dirPath);
            var alienFile = CreateSourceFile(root: dirPath);

            // Create a file alien to the build under the directory we will enumerate
            File.WriteAllText(alienFile.Path.ToString(Context.PathTable), "some text");

            var operations = new List<Operation>
            {
                Operation.EnumerateDir(dirToEnumerate, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact()) // dummy output
            };

            if (isUndeclaredRead)
            {
                operations.Insert(0, Operation.ReadFile(alienFile, doNotInfer: true));
            }

            var builder = CreatePipBuilder(operations);

            // This makes sure we use the right file system, which is aware of alien files
            builder.Options |= global::BuildXL.Pips.Operations.Process.Options.AllowUndeclaredSourceReads;

            var pip = SchedulePipBuilder(builder);

            // Run once
            RunScheduler().AssertSuccess();
            // Delete the alien file under the enumerated directory. We should get a miss on re-run
            File.Delete(alienFile.Path.ToString(Context.PathTable));
            RunScheduler().AssertCacheMiss(pip.Process.PipId);
        }

        [Fact]
        public void AlienFilesInsideDirectoryArePartOfTheFingerprint()
        {
            string dir = Path.Combine(SourceRoot, "dir");
            AbsolutePath dirPath = AbsolutePath.Create(Context.PathTable, dir);
            DirectoryArtifact dirToEnumerate = DirectoryArtifact.CreateWithZeroPartialSealId(dirPath);
            
            // Create the directory to be enumerated with one file inside
            FileUtilities.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "file.txt"), "some text");

            var operations = new List<Operation>
            {
                Operation.EnumerateDir(dirToEnumerate, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact()) // dummy output
            };

            var builder = CreatePipBuilder(operations);
            // This makes sure we use the right file system, which is aware of alien files
            builder.Options |= global::BuildXL.Pips.Operations.Process.Options.AllowUndeclaredSourceReads;

            // Run once
            var pip = SchedulePipBuilder(builder);
            RunScheduler().AssertSuccess();

            // Create a file under a nested dir
            string nestedDir = Path.Combine(dir, "nested");
            FileUtilities.CreateDirectory(nestedDir);
            File.WriteAllText(Path.Combine(nestedDir, "another-file.txt"), "some text");

            // The presence of the new file under a nested dir should trigger a cache miss
            RunScheduler().AssertCacheMiss(pip.Process.PipId);
        }

        [Fact]
        public void StaleSharedOpaqueOutputsAreNotPartOfTheFingerprint()
        {
            AbsolutePath dirPath = AbsolutePath.Create(Context.PathTable, Path.Combine(SourceRoot, "dir"));
            DirectoryArtifact dirToEnumerate = DirectoryArtifact.CreateWithZeroPartialSealId(dirPath);
            var alienFile = CreateSourceFile(root: dirPath);
            string alienFilePath = alienFile.Path.ToString(Context.PathTable);

            // Create a fake stale shared opaque output under the directory we will enumerate
            File.WriteAllText(alienFilePath, "some text");
            SharedOpaqueOutputHelper.EnforceFileIsSharedOpaqueOutput(alienFilePath);

            // A stale shared opaque present when the pip is run is only compatible with lazy SO deletion
            Configuration.Schedule.UnsafeLazySODeletion = true;

            var builder = CreatePipBuilder(new Operation[]
            {
                Operation.EnumerateDir(dirToEnumerate, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact()) // dummy output
            });

            // This makes sure we use the right file system, which is aware of alien files
            builder.Options |= global::BuildXL.Pips.Operations.Process.Options.AllowUndeclaredSourceReads;

            var pip = SchedulePipBuilder(builder);

            // Run once
            RunScheduler().AssertSuccess();
            // Delete the sharedOpaque under the enumerated directory. We should get a cache hit on re-run since
            // old outputs are ignored
            File.Delete(alienFile.Path.ToString(Context.PathTable));
            RunScheduler().AssertCacheHit(pip.Process.PipId);
        }

        [Fact]
        public void GlobalUntrackedScopesAreNotPartOfTheFingerprint()
        {
            AbsolutePath dirPath = AbsolutePath.Create(Context.PathTable, Path.Combine(SourceRoot, "dir"));
            DirectoryArtifact dirToEnumerate = DirectoryArtifact.CreateWithZeroPartialSealId(dirPath);

            // Create a directory we'll later globally untrack
            var globalUntrackedScope = DirectoryArtifact.CreateWithZeroPartialSealId(dirPath.Combine(Context.PathTable, "globalUntrackedDir"));
            Directory.CreateDirectory(globalUntrackedScope.Path.ToString(Context.PathTable));

            var builder = CreatePipBuilder(new Operation[]
            {
                Operation.EnumerateDir(dirToEnumerate, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact()) // dummy output
            });
            
            Configuration.Sandbox.GlobalUnsafeUntrackedScopes = new List<AbsolutePath> { globalUntrackedScope.Path };

            // This makes sure we use the right file system, which is aware of alien files
            builder.Options |= global::BuildXL.Pips.Operations.Process.Options.AllowUndeclaredSourceReads;

            var pip = SchedulePipBuilder(builder);

            // Run once
            RunScheduler().AssertSuccess();
            
            // Delete the untracked scope. We should get a cache hit on re-run 
            Directory.Delete(globalUntrackedScope.Path.ToString(Context.PathTable));

            RunScheduler().AssertCacheHit(pip.Process.PipId);
        }

        [Fact]
        public void ExistingDirectoriesArePartOfTheFingerprint()
        {
            string dir = Path.Combine(SourceRoot, "dir");
            AbsolutePath dirPath = AbsolutePath.Create(Context.PathTable, dir);
            DirectoryArtifact dirToEnumerate = DirectoryArtifact.CreateWithZeroPartialSealId(dirPath);

            // Create a directory nested into the one that is going to be enumerated
            string nestedDir = Path.Combine(dir, "nested");
            Directory.CreateDirectory(nestedDir);

            var operations = new List<Operation>
            {
                Operation.EnumerateDir(dirToEnumerate, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact()) // dummy output
            };

            var builder = CreatePipBuilder(operations);

            // This makes sure we use the right file system, which is aware of alien files
            builder.Options |= global::BuildXL.Pips.Operations.Process.Options.AllowUndeclaredSourceReads;

            var pip = SchedulePipBuilder(builder);

            // Run once
            RunScheduler().AssertSuccess();

            // Delete the directory. We should get a miss on re-run since directories not created by this pip are part of the fingerprint
            Directory.Delete(nestedDir);
            RunScheduler().AssertCacheMiss(pip.Process.PipId);
        }

        /// <summary>
        /// Requiring Windows for this test is not actually a limitation of the enumeration mode (which should work fine in non-windows OSs) but
        /// a limitation of <see cref="IScheduleConfiguration.TreatAbsentDirectoryAsExistentUnderOpaque"/>, where directory probes cannot be simulated
        /// Creating a directory (see below) involves probing.
        /// </summary>
        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void CreatedDirectoriesUnderSharedOpaquesAreNotPartOfTheFingerprint()
        {
            string dir = Path.Combine(SourceRoot, "dir");
            AbsolutePath dirPath = AbsolutePath.Create(Context.PathTable, dir);
            DirectoryArtifact dirToEnumerate = DirectoryArtifact.CreateWithZeroPartialSealId(dirPath);

            Configuration.Logging.CacheMissAnalysisOption = CacheMissAnalysisOption.LocalMode();

            AbsolutePath nestedDirPath = dirPath.Combine(Context.PathTable, "nested");
            var nestedDir = DirectoryArtifact.CreateWithZeroPartialSealId(nestedDirPath);

            var operations = new List<Operation>
            {
                // Create a directory nested into the one that is going to be enumerated
                Operation.CreateDir(nestedDir, doNotInfer: true),
                Operation.EnumerateDir(dirToEnumerate, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact()) // dummy output
            };

            var builder = CreatePipBuilder(operations);

            // This makes sure we use the right file system, which is aware of alien files
            builder.Options |= global::BuildXL.Pips.Operations.Process.Options.AllowUndeclaredSourceReads;
            // Define the shared opaque
            builder.AddOutputDirectory(dirPath, global::BuildXL.Pips.Operations.SealDirectoryKind.SharedOpaque);

            var pip = SchedulePipBuilder(builder);

            // Run once
            RunScheduler().AssertSuccess();

            // Simulate shared opaque scrubbing
            FileUtilities.DeleteDirectoryContents(nestedDirPath.ToString(Context.PathTable), deleteRootDirectory: true);

            // This should be a cache hit. Directories created by a pip are not part of the fingerprint
            RunScheduler().AssertCacheHit(pip.Process.PipId);
        }

        /// <summary>
        /// Requiring Windows for this test is not actually a limitation of the enumeration mode (which should work fine in non-windows OSs) but
        /// a limitation of <see cref="IScheduleConfiguration.TreatAbsentDirectoryAsExistentUnderOpaque"/>, where directory probes cannot be simulated
        /// Creating a directory (see below) involves probing.
        /// </summary>
        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void FingerprintIsStableForCreatedDirectories()
        {
            string dir = Path.Combine(SourceRoot, "dir");
            AbsolutePath dirPath = AbsolutePath.Create(Context.PathTable, dir);
            DirectoryArtifact dirToEnumerate = DirectoryArtifact.CreateWithZeroPartialSealId(dirPath);

            Configuration.Logging.CacheMissAnalysisOption = CacheMissAnalysisOption.LocalMode();

            AbsolutePath nestedDirPath = dirPath.Combine(Context.PathTable, "nested");
            var nestedDir = DirectoryArtifact.CreateWithZeroPartialSealId(nestedDirPath);
            var outputInNestedDor = CreateOutputFileArtifact(nestedDirPath.ToString(Context.PathTable));

            // Create a pip that creates the directory to be enumerated
            var dirCreatorBuilder = CreatePipBuilder(new List<Operation>
            {
                // Create a directory nested into the one that is going to be enumerated
                Operation.CreateDir(nestedDir, doNotInfer: true),
                // Create a file underneath
                Operation.WriteFile(outputInNestedDor, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact()) // dummy output
            });

            // This makes sure we use the right file system, which is aware of alien files
            dirCreatorBuilder.Options |= global::BuildXL.Pips.Operations.Process.Options.AllowUndeclaredSourceReads;
            // Define the shared opaque
            dirCreatorBuilder.AddOutputDirectory(dirPath, global::BuildXL.Pips.Operations.SealDirectoryKind.SharedOpaque);

            var dirCreator = SchedulePipBuilder(dirCreatorBuilder);

            // Create a pip that enumerates the directory
            var dirEnumeratorBuilder = CreatePipBuilder(new List<Operation>
            {
                Operation.EnumerateDir(dirToEnumerate, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact()) // dummy output
            });

            // This makes sure we use the right file system, which is aware of alien files
            dirEnumeratorBuilder.Options |= global::BuildXL.Pips.Operations.Process.Options.AllowUndeclaredSourceReads;
            // Define the shared opaque
            dirEnumeratorBuilder.AddOutputDirectory(dirPath, global::BuildXL.Pips.Operations.SealDirectoryKind.SharedOpaque);
            // Make the enumerator depend on the creator
            dirEnumeratorBuilder.AddInputFile(dirCreator.ProcessOutputs.GetRequiredOutputFiles().Single());

            var dirEnumerator = SchedulePipBuilder(dirEnumeratorBuilder);

            // Run once. The created directory should be ignored for the enumeration fingerprint computation.
            RunScheduler().AssertSuccess();

            // Simulate shared opaque scrubbing
            FileUtilities.DeleteDirectoryContents(nestedDirPath.ToString(Context.PathTable), deleteRootDirectory: true);

            // This should be a cache hit. By replaying the dirCreator pip, the enumerated directory is re-created. However, 
            // it should still be ignored by the fingerprint computation and we should get a cache hit.
            RunScheduler().AssertCacheHit(dirEnumerator.Process.PipId);
        }

        [Fact]
        public void ImmediateDependencyOutputsArePartOfTheFingerprint()
        {
            // Schedule a pip that writes a file
            var outputFile = CreateOutputFileArtifact();
            var writer = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(outputFile)
            });

            var writePip = SchedulePipBuilder(writer);

            // Schedule a pip that depends on the writer and enumerates the dir containing the written file
            AbsolutePath dirPath = outputFile.Path.GetParent(Context.PathTable);
            DirectoryArtifact dirToEnumerate = DirectoryArtifact.CreateWithZeroPartialSealId(dirPath);

            var dummyOutput = CreateOutputFileArtifact();
            var enumerator = CreatePipBuilder(new Operation[]
            {
                Operation.EnumerateDir(dirToEnumerate, doNotInfer: true),
                Operation.ReadFile(writePip.ProcessOutputs.GetOutputFile(outputFile)),
                Operation.WriteFile(dummyOutput)
            });

            // This makes sure we use the right file system, which is aware of alien files
            enumerator.Options |= global::BuildXL.Pips.Operations.Process.Options.AllowUndeclaredSourceReads;

            var enumeratorPip = SchedulePipBuilder(enumerator);

            // Set lazy materialization and a filter, so the writer file doesn't have to be materialized if the enumerator pip is a hit
            Configuration.Schedule.EnableLazyOutputMaterialization = true;
            Configuration.Filter = $"output='*{Path.DirectorySeparatorChar}{dummyOutput.Path.GetName(Context.PathTable).ToString(Context.StringTable)}'";

            // Run once
            RunScheduler().AssertSuccess();

            // Delete the produced file. However, we should get a hit because the fingerprint should still include the intermediate output
            // even if it is not on disk
            File.Delete(outputFile.Path.ToString(Context.PathTable));
            RunScheduler().AssertCacheHit(enumeratorPip.Process.PipId);

            // Make sure the intermediate output was in fact not produced
            Assert.False(File.Exists(outputFile.Path.ToString(Context.PathTable)));
        }

        [Fact]
        public void AlienEnumerationCacheIsValid()
        {
            AbsolutePath dirPath = AbsolutePath.Create(Context.PathTable, Path.Combine(SourceRoot, "dir"));
            DirectoryArtifact dirToPathArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(dirPath);
            var sourceFile = CreateSourceFile(root: dirPath);

            // This is a pip that statically declares a source file and enumerates the directory that contains it
            var writer = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(sourceFile),
                Operation.EnumerateDir(dirToPathArtifact, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact()) // dummy output
            });

            // This makes sure we use the right file system, which is aware of alien files
            writer.Options |= global::BuildXL.Pips.Operations.Process.Options.AllowUndeclaredSourceReads;

            var writePip = SchedulePipBuilder(writer);

            // This pip enumerates the same dir containing the source file
            var enumerator = CreatePipBuilder(new Operation[]
            {
                Operation.EnumerateDir(dirToPathArtifact, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact()) // dummy output
            });

            // This makes sure we use the right file system, which is aware of alien files
            enumerator.Options |= global::BuildXL.Pips.Operations.Process.Options.AllowUndeclaredSourceReads;

            var enumeratorPip = SchedulePipBuilder(enumerator);

            // Run once. Since writePip enumerates the dir, the alien enumeration is cached. EnumeratorPip runs afterwards and reuses the cached value, since it is enumerating the same dir.
            // writePip has the source file coming from the pip filesystem (since it is statically declared) and enumeratorPip hasn't.
            RunScheduler(constraintExecutionOrder:new[] {((Pip)writePip.Process, (Pip)enumeratorPip.Process)}).AssertSuccess();

            // Run again but in opposite order. We should get a cache hit, which validates the alien enumeration caching is stable regardless of the outcome of the pip filesystem
            RunScheduler(constraintExecutionOrder: new[] { ((Pip)enumeratorPip.Process, (Pip)writePip.Process) }).AssertSuccess().AssertCacheHit(enumeratorPip.Process.PipId, writePip.Process.PipId);
        }

        [Fact]
        public void KnownOutputsOutsideImmediateDependenciesAreNotPartOfTheFingerprint()
        {
            // Schedule a pip that writes a file
            var outputFile = CreateOutputFileArtifact();
            var writer = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(outputFile)
            });

            var writePip = SchedulePipBuilder(writer);

            // Schedule a pip that doesn't depend on the writer and enumerates the dir containing the writer file
            AbsolutePath dirPath = outputFile.Path.GetParent(Context.PathTable);
            DirectoryArtifact dirToEnumerate = DirectoryArtifact.CreateWithZeroPartialSealId(dirPath);

            var dummyOutput = CreateOutputFileArtifact();
            var enumerator = CreatePipBuilder(new Operation[]
            {
                Operation.EnumerateDir(dirToEnumerate, doNotInfer: true),
                Operation.WriteFile(dummyOutput)
            });

            // This makes sure we use the right file system, which is aware of alien files
            enumerator.Options |= global::BuildXL.Pips.Operations.Process.Options.AllowUndeclaredSourceReads;

            var enumeratorPip = SchedulePipBuilder(enumerator);

            // Configure a filter so only the enumerator pip runs
            Configuration.Filter = $"output='*{Path.DirectorySeparatorChar}{dummyOutput.Path.GetName(Context.PathTable).ToString(Context.StringTable)}'";

            // Run once
            RunScheduler().AssertSuccess();

            // Make sure the file produced by the writer was actually never produced
            Assert.False(File.Exists(outputFile.Path.ToString(Context.PathTable)));

            // Now re-create the file as if it was produced
            File.WriteAllText(outputFile.Path.ToString(Context.PathTable), "some content");

            // We should get a cache hit: even though the output produced by the writer pip is now there,
            // it shouldn't have been part of the fingerprint to begin with
            RunScheduler().AssertCacheHit(enumeratorPip.Process.PipId);
        }

        [FactIfSupported(requiresSymlinkPermission: true, requiresWindowsBasedOperatingSystem: true)]
        public void EnumeratedReparsePointIsTreatedAsFile()
        {
            // Create sharedOpaque/junctionDir -> target
            string sharedOpaqueDirString = Path.Combine(ObjectRoot, "sod");
            string junctionDirString = Path.Combine(sharedOpaqueDirString, "junction");
            string targetDirString = Path.Combine(ObjectRoot, "target");

            Directory.CreateDirectory(sharedOpaqueDirString);
            Directory.CreateDirectory(targetDirString);
            FileUtilities.CreateJunction(junctionDirString, targetDirString, createDirectoryForJunction: true);

            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDirString);
            AbsolutePath junctionDirPath = AbsolutePath.Create(Context.PathTable, junctionDirString);
            DirectoryArtifact sharedOpaqueDir = DirectoryArtifact.CreateWithZeroPartialSealId(sharedOpaqueDirPath);

            // Schedule a pip that enumerates the shared opaque, turning on undeclared source reads so we make sure minimal graph with alien files is used
            var enumeratorBuilder = CreatePipBuilder(new Operation[]
            {
                Operation.EnumerateDir(sharedOpaqueDir, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact())
            });

            enumeratorBuilder.AddOutputDirectory(sharedOpaqueDir, global::BuildXL.Pips.Operations.SealDirectoryKind.SharedOpaque);
            enumeratorBuilder.Options |= global::BuildXL.Pips.Operations.Process.Options.AllowUndeclaredSourceReads;

            SchedulePipBuilder(enumeratorBuilder);

            var result = RunScheduler().AssertSuccess();

            var existence = result.FileSystemView.GetExistence(junctionDirPath, global::BuildXL.Scheduler.FileSystem.FileSystemViewMode.Real);

            XAssert.AreEqual(PathExistence.ExistsAsFile, existence.Result);
        }
    }
}
