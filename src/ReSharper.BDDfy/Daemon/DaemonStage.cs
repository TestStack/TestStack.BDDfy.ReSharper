using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Application.Progress;
using JetBrains.Application.Settings;
using JetBrains.ReSharper.Daemon;
using JetBrains.ReSharper.Daemon.CSharp.Stages;
using JetBrains.ReSharper.Daemon.UsageChecking;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Caches;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.Resolve;
using JetBrains.ReSharper.Psi.Search;
using JetBrains.Util;

namespace ReSharper.BDDfy.Daemon
{
    [DaemonStage(StagesBefore = new[] { typeof(LanguageSpecificDaemonStage) })]
    public class DaemonStage : CSharpDaemonStageBase
    {
        private readonly SearchDomainFactory searchDomainFactory;

        public DaemonStage(SearchDomainFactory searchDomainFactory)
        {
            this.searchDomainFactory = searchDomainFactory;
        }

        protected override IDaemonStageProcess CreateProcess(IDaemonProcess process, IContextBoundSettingsStore settings, DaemonProcessKind processKind, ICSharpFile file)
        {
            return new DaemonStageProcess(process, searchDomainFactory, file);
        }
    }

    public class DaemonStageProcess : IDaemonStageProcess
    {
        private readonly SearchDomainFactory searchDomainFactory;
        private readonly ICSharpFile csFile;
        private readonly CollectUsagesStageProcess usages;

        public DaemonStageProcess(IDaemonProcess process, SearchDomainFactory searchDomainFactory, ICSharpFile csFile)
        {
            this.searchDomainFactory = searchDomainFactory;
            this.csFile = csFile;
            DaemonProcess = process;
            usages = process.GetStageProcess<CollectUsagesStageProcess>();
            Assertion.Assert(usages != null, "usages != null");
        }

        public void Execute(Action<DaemonStageResult> committer)
        {
            IPsiSourceFile sourceFile = DaemonProcess.SourceFile;
            IPsiServices psiServices = sourceFile.GetPsiServices();
            ISymbolScope symbolScope = psiServices.Symbols.GetSymbolScope(LibrarySymbolScope.FULL, false, sourceFile.ResolveContext);

            ITypeElement typeElement = symbolScope.GetTypeElementByCLRName("TestStack.BDDfy.BDDfyExtensions");
            if (typeElement == null) return;

            IEnumerable<IMethod> bddfyMethods = typeElement.Methods.Where(method => method.ShortName == "BDDfy" || method.ShortName == "LazyBDDfy");
            ISearchDomain searchDomain = searchDomainFactory.CreateSearchDomain(sourceFile);
            IReference[] references = bddfyMethods.SelectMany(method => psiServices.Finder.FindReferences(method, searchDomain, NullProgressIndicator.Instance)).ToArray();
            foreach (IReference reference in references)
            {
                var node = reference.GetTreeNode() as ICSharpTreeNode;
                if (node != null)
                {
                    var classDeclaration = node.GetContainingTypeDeclaration() as IClassDeclaration;
                    if (classDeclaration != null)
                    {
                        SetClassAndMembersUsed(classDeclaration);
                    }
                }
            }
        }

        private void SetClassAndMembersUsed(IClassDeclaration classDeclaration)
        {
            ITypeElement typeElement = classDeclaration.DeclaredElement;
            if (typeElement == null || (typeElement is IClass == false))
            {
                return;
            }

            foreach (ITypeMember typeMember in typeElement.GetMembers())
            {
                var method = typeMember as IMethod;
                if (method != null)
                {
                    usages.SetElementState(method, UsageState.ALL_MASK);
                }
            }

            usages.SetElementState(typeElement, UsageState.ALL_MASK);
        }

        public IDaemonProcess DaemonProcess { get; private set; }
    }
}