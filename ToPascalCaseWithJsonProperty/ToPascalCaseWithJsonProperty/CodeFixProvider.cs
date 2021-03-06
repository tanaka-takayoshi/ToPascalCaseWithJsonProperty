﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;
using System.Globalization;

namespace ToPascalCaseWithJsonProperty
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ToPascalCaseWithJsonPropertyCodeFixProvider)), Shared]
    public class ToPascalCaseWithJsonPropertyCodeFixProvider : CodeFixProvider
    {
        private SyntaxToken whitespaceToken =
        SyntaxFactory.Token(SyntaxTriviaList.Create(SyntaxFactory.Space), SyntaxKind.StringLiteralToken, SyntaxTriviaList.Empty);


        public sealed override ImmutableArray<string> FixableDiagnosticIds
        {
            get { return ImmutableArray.Create(ToPascalCaseWithJsonPropertyAnalyzer.DiagnosticId); }
        }

        public sealed override FixAllProvider GetFixAllProvider()
        {
            return WellKnownFixAllProviders.BatchFixer;
        }

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            // TODO: Replace the following code with your own analysis, generating a CodeAction for each fix to suggest
            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;

            // Find the type declaration identified by the diagnostic.
            var declaration = root.FindToken(diagnosticSpan.Start).Parent.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().First();

            // Register a code action that will invoke the fix.
            context.RegisterCodeFix(
                CodeAction.Create("To PascalCase with Json.Net", c => MakeUppercaseAsync(context.Document, declaration, c)),
                diagnostic);
        }

        private async Task<Document> MakeUppercaseAsync(Document document, TypeDeclarationSyntax typeDecl, CancellationToken cancellationToken)
        {
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            var root = await document.GetSyntaxRootAsync(cancellationToken);
            var newRoot = root;
            var names = typeDecl.ChildNodes()
                .OfType<PropertyDeclarationSyntax>()
                .Select(p => p.Identifier.Text);

            foreach (var name in names)
            {
                //最新のドキュメントルートから変更対象のプロパティを名前から探す。
                //ドキュメントルートは構築しなおされているので、SyntaxNodeの参照一致では見つけられない
                var property = newRoot.DescendantNodes()
                    .OfType<TypeDeclarationSyntax>()
                    .First(t => t.Identifier.Text == typeDecl.Identifier.Text)
                    .ChildNodes()
                    .OfType<PropertyDeclarationSyntax>()
                    .First(p => p.Identifier.Text == name);
                var previousName = property.Identifier.Text;
                var newName = ToPascal(previousName);
                //すでにPascal Caseなら無視
                if (previousName == newName)
                    continue;
                //新しいプロパティを構築
                var newProperty = property
                    .WithIdentifier(SyntaxFactory.Identifier(newName))
                    .AddAttributeLists(
                        SyntaxFactory.AttributeList(
                            SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.Attribute(SyntaxFactory.ParseName("JsonProperty"))
                                .AddArgumentListArguments(
                                    SyntaxFactory.AttributeArgument(
                                        SyntaxFactory.LiteralExpression(
                                            SyntaxKind.StringLiteralExpression, 
                                            SyntaxFactory.Literal(previousName)))                                    
                                )
                            )
                        )
                    );
                newRoot = newRoot.ReplaceNode(property, new[] { newProperty });
            }

            if (newRoot.ChildNodes()
                .OfType<UsingDirectiveSyntax>()
                .All(u => (u.Name as QualifiedNameSyntax)?.ToFullString() != "Newtonsoft.Json"))
            {
                var lastUsing = newRoot.DescendantNodes()
                        .OfType<UsingDirectiveSyntax>()
                        .LastOrDefault();
                var insertingUsing = SyntaxFactory.UsingDirective(
                    SyntaxFactory.IdentifierName("Newtonsoft.Json"));
                if (lastUsing == null)
                {
                    //TODO using が1つもない場合に挿入する方法がわからない
                    //newRoot = newRoot.InsertNodesBefore(newRoot.ChildNodes().First(), new[] {insertingUsing});
                }
                else
                {
                    newRoot = newRoot.InsertNodesAfter(lastUsing, new[] { insertingUsing });
                }
            }
            return document.WithSyntaxRoot(newRoot);
        }

        private string ToPascal(string name)
        {
            var textInfo = new CultureInfo("en-US").TextInfo;
            var titleCaseStr = textInfo.ToTitleCase(name);
            return titleCaseStr.Replace("_", string.Empty);
        }
    }
}