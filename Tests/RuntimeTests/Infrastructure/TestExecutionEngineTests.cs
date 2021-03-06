﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Moq;
using NUnit.Framework;
using TechTalk.SpecFlow.Bindings;
using TechTalk.SpecFlow.Configuration;
using TechTalk.SpecFlow.ErrorHandling;
using TechTalk.SpecFlow.Infrastructure;
using TechTalk.SpecFlow.Tracing;
using TechTalk.SpecFlow.UnitTestProvider;
using Should;

namespace TechTalk.SpecFlow.RuntimeTests.Infrastructure
{
    [TestFixture]
    public class TestExecutionEngineTests
    {
        private ScenarioContext scenarioContext;
        private RuntimeConfiguration runtimeConfiguration;
        private Mock<IBindingRegistry> bindingRegistryStub;
        private Mock<IErrorProvider> errorProviderStub;
        private Mock<IContextManager> contextManagerStub;
        private Mock<ITestTracer> testTracerStub;
        private Mock<IStepDefinitionMatcher> stepDefinitionMatcherStub;
        private Dictionary<ProgrammingLanguage, IStepDefinitionSkeletonProvider> skeletonProviders;
        private Dictionary<string, IStepErrorHandler> stepErrorHandlers;

        private readonly List<IHookBinding> beforeStepEvents = new List<IHookBinding>();
        private readonly List<IHookBinding> afterStepEvents = new List<IHookBinding>();
        private readonly List<IHookBinding> beforeScenarioBlockEvents = new List<IHookBinding>();
        private readonly List<IHookBinding> afterScenarioBlockEvents = new List<IHookBinding>();

        [SetUp]
        public void Setup()
        {
            skeletonProviders = new Dictionary<ProgrammingLanguage, IStepDefinitionSkeletonProvider>();
            skeletonProviders.Add(ProgrammingLanguage.CSharp, new Mock<IStepDefinitionSkeletonProvider>().Object);

            var culture = new CultureInfo("en-US");
            contextManagerStub = new Mock<IContextManager>();
            scenarioContext = new ScenarioContext(new ScenarioInfo("scenario_title"), null, null);
            contextManagerStub.Setup(cm => cm.ScenarioContext).Returns(scenarioContext);
            contextManagerStub.Setup(cm => cm.FeatureContext).Returns(new FeatureContext(new FeatureInfo(culture, "feature_title", "", ProgrammingLanguage.CSharp), culture));

            bindingRegistryStub = new Mock<IBindingRegistry>();
            bindingRegistryStub.Setup(br => br.GetEvents(BindingEvent.StepStart)).Returns(beforeStepEvents);
            bindingRegistryStub.Setup(br => br.GetEvents(BindingEvent.StepEnd)).Returns(afterStepEvents);
            bindingRegistryStub.Setup(br => br.GetEvents(BindingEvent.BlockStart)).Returns(beforeScenarioBlockEvents);
            bindingRegistryStub.Setup(br => br.GetEvents(BindingEvent.BlockEnd)).Returns(afterScenarioBlockEvents);

            runtimeConfiguration = new RuntimeConfiguration();
            errorProviderStub = new Mock<IErrorProvider>();
            testTracerStub = new Mock<ITestTracer>();
            stepDefinitionMatcherStub = new Mock<IStepDefinitionMatcher>();

            stepErrorHandlers = new Dictionary<string, IStepErrorHandler>();
        }

        private TestExecutionEngine CreateTestExecutionEngine()
        {
            return new TestExecutionEngine(
                new Mock<IStepFormatter>().Object, 
                testTracerStub.Object, 
                errorProviderStub.Object, 
                new Mock<IStepArgumentTypeConverter>().Object, 
                runtimeConfiguration, 
                bindingRegistryStub.Object,
                new Mock<IUnitTestRuntimeProvider>().Object,
                skeletonProviders, 
                contextManagerStub.Object, 
                stepDefinitionMatcherStub.Object, 
                stepErrorHandlers);
        }

        private Mock<IStepDefinitionBinding> RegisterStepDefinition()
        {
            var stepDefStub = new Mock<IStepDefinitionBinding>();
            stepDefinitionMatcherStub.Setup(sdm => sdm.GetMatches(It.IsAny<StepArgs>())).Returns((StepArgs sa) =>
                      new List<BindingMatch> { new BindingMatch(stepDefStub.Object, sa, new string[0], new string[0], 0) });

            return stepDefStub;
        }


        private void RegisterFailingStepDefinition()
        {
            var stepDefStub = RegisterStepDefinition();
            TimeSpan duration;
            stepDefStub.Setup(sd => sd.Invoke(contextManagerStub.Object, testTracerStub.Object, It.IsAny<object[]>(), out duration))
                .Throws(new Exception("simulated error"));
        }

        private Mock<IHookBinding> CreateHookMock(List<IHookBinding> hookList)
        {
            var mock = new Mock<IHookBinding>();
            hookList.Add(mock.Object);
            return mock;
        }

        [Test]
        public void Should_execute_before_step()
        {
            var testExecutionEngine = CreateTestExecutionEngine();
            RegisterStepDefinition();

            var hookMock = CreateHookMock(beforeStepEvents);

            testExecutionEngine.Step(StepDefinitionKeyword.Given, "foo", null, null);

            hookMock.Verify(sm => sm.Invoke(contextManagerStub.Object, testTracerStub.Object), Times.Once());
        }

        [Test]
        public void Should_execute_after_step()
        {
            var testExecutionEngine = CreateTestExecutionEngine();
            RegisterStepDefinition();

            var hookMock = CreateHookMock(afterStepEvents);

            testExecutionEngine.Step(StepDefinitionKeyword.Given, "foo", null, null);

            hookMock.Verify(sm => sm.Invoke(contextManagerStub.Object, testTracerStub.Object), Times.Once());
        }

        [Test]
        public void Should_not_execute_step_when_there_was_an_error_earlier()
        {
            var testExecutionEngine = CreateTestExecutionEngine();
            var stepDefMock = RegisterStepDefinition();

            scenarioContext.TestStatus = TestStatus.TestError;

            testExecutionEngine.Step(StepDefinitionKeyword.Given, "foo", null, null);

            TimeSpan duration;
            stepDefMock.Verify(sm => sm.Invoke(It.IsAny<IContextManager>(), It.IsAny<ITestTracer>(), It.IsAny<object[]>(), out duration), Times.Never());
        }

        [Test]
        public void Should_not_execute_step_hooks_when_there_was_an_error_earlier()
        {
            var testExecutionEngine = CreateTestExecutionEngine();
            RegisterStepDefinition();

            scenarioContext.TestStatus = TestStatus.TestError;

            var beforeStepMock = CreateHookMock(beforeStepEvents);
            var afterStepMock = CreateHookMock(afterStepEvents);

            testExecutionEngine.Step(StepDefinitionKeyword.Given, "foo", null, null);

            beforeStepMock.Verify(sm => sm.Invoke(It.IsAny<IContextManager>(), It.IsAny<ITestTracer>()), Times.Never());
            afterStepMock.Verify(sm => sm.Invoke(It.IsAny<IContextManager>(), It.IsAny<ITestTracer>()), Times.Never());
        }

        [Test]
        public void Should_execute_after_step_when_step_definition_failed()
        {
            var testExecutionEngine = CreateTestExecutionEngine();
            RegisterFailingStepDefinition();

            var hookMock = CreateHookMock(afterStepEvents);

            testExecutionEngine.Step(StepDefinitionKeyword.Given, "foo", null, null);

            hookMock.Verify(sm => sm.Invoke(contextManagerStub.Object, testTracerStub.Object));
        }

        [Test]
        public void Should_call_step_error_handlers()
        {
            var stepErrorHandlerMock = new Mock<IStepErrorHandler>();
            stepErrorHandlers.Add("eh1", stepErrorHandlerMock.Object);

            var testExecutionEngine = CreateTestExecutionEngine();
            RegisterFailingStepDefinition();

            testExecutionEngine.Step(StepDefinitionKeyword.Given, "foo", null, null);

            stepErrorHandlerMock.Verify(seh => seh.OnStepFailure(testExecutionEngine, It.IsAny<StepFailureEventArgs>()), Times.Once());
        }


        [Test]
        public void Should_call_multiple_step_error_handlers()
        {
            var stepErrorHandler1Mock = new Mock<IStepErrorHandler>();
            var stepErrorHandler2Mock = new Mock<IStepErrorHandler>();
            stepErrorHandlers.Add("eh1", stepErrorHandler1Mock.Object);
            stepErrorHandlers.Add("eh2", stepErrorHandler2Mock.Object);

            var testExecutionEngine = CreateTestExecutionEngine();
            RegisterFailingStepDefinition();

            testExecutionEngine.Step(StepDefinitionKeyword.Given, "foo", null, null);

            stepErrorHandler1Mock.Verify(seh => seh.OnStepFailure(testExecutionEngine, It.IsAny<StepFailureEventArgs>()), Times.Once());
            stepErrorHandler2Mock.Verify(seh => seh.OnStepFailure(testExecutionEngine, It.IsAny<StepFailureEventArgs>()), Times.Once());
        }

        [Test]
        public void Should_be_able_to_swallow_error_in_step_error_handlers()
        {
            var stepErrorHandlerStub = new Mock<IStepErrorHandler>();
            stepErrorHandlers.Add("eh1", stepErrorHandlerStub.Object);

            stepErrorHandlerStub.Setup(seh => seh.OnStepFailure(It.IsAny<TestExecutionEngine>(), It.IsAny<StepFailureEventArgs>()))
                .Callback((TestExecutionEngine _, StepFailureEventArgs args) => args.IsHandled = true);

            var testExecutionEngine = CreateTestExecutionEngine();
            RegisterFailingStepDefinition();

            testExecutionEngine.Step(StepDefinitionKeyword.Given, "foo", null, null);

            scenarioContext.TestStatus.ShouldEqual(TestStatus.OK);
        }

        [Test]
        public void Step_error_handlers_should_not_swallow_error_by_default()
        {
            var stepErrorHandlerStub = new Mock<IStepErrorHandler>();
            stepErrorHandlers.Add("eh1", stepErrorHandlerStub.Object);

            var testExecutionEngine = CreateTestExecutionEngine();
            RegisterFailingStepDefinition();

            testExecutionEngine.Step(StepDefinitionKeyword.Given, "foo", null, null);

            scenarioContext.TestStatus.ShouldEqual(TestStatus.TestError);
        }
    }
}
