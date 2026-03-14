workflow-engine  | 2026-03-14 12:15:44.919 | info: WorkflowEngine.Startup[0]
workflow-engine  | 2026-03-14 12:15:44.919 |       Starting WorkflowEngine (Environment=Development)
workflow-engine  | 2026-03-14 12:15:45.103 | info: WorkflowEngine.Startup[0]
workflow-engine  | 2026-03-14 12:15:45.103 |       Applying WorkflowEngine database migrations
workflow-engine  | 2026-03-14 12:15:47.280 | info: WorkflowEngine.Startup[0]
workflow-engine  | 2026-03-14 12:15:47.280 |       WorkflowEngine database migrations completed
workflow-engine  | 2026-03-14 12:15:47.295 | info: WorkflowEngine.Startup[0]
workflow-engine  | 2026-03-14 12:15:47.295 |       WorkflowEngine is ready and accepting requests
workflow-engine  | 2026-03-14 12:15:47.334 | warn: Microsoft.AspNetCore.DataProtection.Repositories.FileSystemXmlRepository[60]
workflow-engine  | 2026-03-14 12:15:47.334 |       Storing keys in a directory '/home/app/.aspnet/DataProtection-Keys' that may not be persisted outside of the container. Protected data will be unavailable when container is destroyed. For more information go to https://aka.ms/aspnet/dataprotectionwarning
workflow-engine  | 2026-03-14 12:15:47.494 | info: MassTransit[0]
workflow-engine  | 2026-03-14 12:15:47.495 |       Configured endpoint workflow-execution, Consumer: ReelForge.WorkflowEngine.Consumers.WorkflowExecutionRequestedConsumer
workflow-engine  | 2026-03-14 12:15:47.507 | info: MassTransit[0]
workflow-engine  | 2026-03-14 12:15:47.507 |       Configured endpoint workflow-stop-requests, Consumer: ReelForge.WorkflowEngine.Consumers.WorkflowExecutionStopRequestedConsumer
workflow-engine  | 2026-03-14 12:15:47.741 | warn: Microsoft.AspNetCore.DataProtection.KeyManagement.XmlKeyManager[35]
workflow-engine  | 2026-03-14 12:15:47.741 |       No XML encryptor configured. Key {46c475b3-a635-43e9-85ca-7f57236b442f} may be persisted to storage in unencrypted form.
workflow-engine  | 2026-03-14 12:15:47.811 | info: ReelForge.WorkflowEngine.Workers.WorkflowWorkerPool[0]
workflow-engine  | 2026-03-14 12:15:47.811 |       WorkflowWorkerPool started
workflow-engine  | 2026-03-14 12:15:47.813 | warn: Microsoft.AspNetCore.Hosting.Diagnostics[15]
workflow-engine  | 2026-03-14 12:15:47.813 |       Overriding HTTP_PORTS '8080' and HTTPS_PORTS ''. Binding to values defined by URLS instead 'http://+:8080'.
workflow-engine  | 2026-03-14 12:15:47.924 | info: Microsoft.Hosting.Lifetime[14]
workflow-engine  | 2026-03-14 12:15:47.924 |       Now listening on: http://[::]:8080
workflow-engine  | 2026-03-14 12:15:47.924 | info: Microsoft.Hosting.Lifetime[0]
workflow-engine  | 2026-03-14 12:15:47.924 |       Application started. Press Ctrl+C to shut down.
workflow-engine  | 2026-03-14 12:15:47.924 | info: Microsoft.Hosting.Lifetime[0]
workflow-engine  | 2026-03-14 12:15:47.924 |       Hosting environment: Development
workflow-engine  | 2026-03-14 12:15:47.924 | info: Microsoft.Hosting.Lifetime[0]
workflow-engine  | 2026-03-14 12:15:47.924 |       Content root path: /app
workflow-engine  | 2026-03-14 12:15:48.087 | info: MassTransit[0]
workflow-engine  | 2026-03-14 12:15:48.088 |       Bus started: rabbitmq://rabbitmq/
workflow-engine  | 2026-03-14 12:15:48.870 | warn: Microsoft.AspNetCore.HttpsPolicy.HttpsRedirectionMiddleware[3]
workflow-engine  | 2026-03-14 12:15:48.870 |       Failed to determine the https port for redirect.
workflow-engine  | 2026-03-14 12:18:20.440 | info: ReelForge.WorkflowEngine.Consumers.WorkflowExecutionRequestedConsumer[0]
workflow-engine  | 2026-03-14 12:18:20.440 |       Received workflow execution request: ExecutionId=5227a5ab-ab88-4405-8310-819fe90a494b, CorrelationId=cde0617e-63c6-45ea-b469-42adc569e692
workflow-engine  | 2026-03-14 12:18:21.033 | info: ReelForge.WorkflowEngine.Execution.WorkflowExecutorService[0]
workflow-engine  | 2026-03-14 12:18:21.033 |       Starting workflow execution 5227a5ab-ab88-4405-8310-819fe90a494b for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 with 3 steps (CorrelationId=cde0617e-63c6-45ea-b469-42adc569e692)
workflow-engine  | 2026-03-14 12:18:21.048 | info: ReelForge.WorkflowEngine.Execution.WorkflowExecutorService[0]
workflow-engine  | 2026-03-14 12:18:21.048 |       Executing step 1 (Parallel) for execution 5227a5ab-ab88-4405-8310-819fe90a494b, attempt 1/3
workflow-engine  | 2026-03-14 12:18:21.150 | info: ReelForge.WorkflowEngine.Execution.StepExecutors.ParallelStepExecutor[0]
workflow-engine  | 2026-03-14 12:18:21.150 |       Parallel step 1: running 3 agents in parallel: ComponentInventoryAnalyzer, DependencyAnalyzer, StyleAndThemeExtractor
rabbitmq         | 2026-03-14 12:15:33.524 | 2026-03-14 11:15:33.520872+00:00 [info] <0.802.0> closing AMQP connection <0.802.0> (172.20.0.7:39138 -> 172.20.0.9:5672 - ReelForge.Inference.Api, vhost: '/', user: 'guest')
rabbitmq         | 2026-03-14 12:15:35.566 | 2026-03-14 11:15:35.566219+00:00 [info] <0.775.0> closing AMQP connection <0.775.0> (172.20.0.8:35714 -> 172.20.0.9:5672 - ReelForge.WorkflowEngine, vhost: '/', user: 'guest')
rabbitmq         | 2026-03-14 12:15:41.961 | 2026-03-14 11:15:41.960997+00:00 [info] <0.1367.0> accepting AMQP connection <0.1367.0> (172.20.0.4:50572 -> 172.20.0.9:5672)
rabbitmq         | 2026-03-14 12:15:42.015 | 2026-03-14 11:15:42.014768+00:00 [info] <0.1367.0> connection <0.1367.0> (172.20.0.4:50572 -> 172.20.0.9:5672): user 'guest' authenticated and granted access to vhost '/'
rabbitmq         | 2026-03-14 12:15:42.059 | 2026-03-14 11:15:42.058538+00:00 [info] <0.1367.0> closing AMQP connection <0.1367.0> (172.20.0.4:50572 -> 172.20.0.9:5672, vhost: '/', user: 'guest')
rabbitmq         | 2026-03-14 12:15:42.095 | 2026-03-14 11:15:42.094714+00:00 [info] <0.1384.0> accepting AMQP connection <0.1384.0> (172.20.0.4:50588 -> 172.20.0.9:5672)
rabbitmq         | 2026-03-14 12:15:42.098 | 2026-03-14 11:15:42.097605+00:00 [info] <0.1384.0> connection <0.1384.0> (172.20.0.4:50588 -> 172.20.0.9:5672) has a client-provided name: ReelForge.Inference.Api
rabbitmq         | 2026-03-14 12:15:42.099 | 2026-03-14 11:15:42.099009+00:00 [info] <0.1384.0> connection <0.1384.0> (172.20.0.4:50588 -> 172.20.0.9:5672 - ReelForge.Inference.Api): user 'guest' authenticated and granted access to vhost '/'
rabbitmq         | 2026-03-14 12:15:47.919 | 2026-03-14 11:15:47.918096+00:00 [info] <0.1402.0> accepting AMQP connection <0.1402.0> (172.20.0.7:56484 -> 172.20.0.9:5672)
rabbitmq         | 2026-03-14 12:15:47.946 | 2026-03-14 11:15:47.946129+00:00 [info] <0.1402.0> connection <0.1402.0> (172.20.0.7:56484 -> 172.20.0.9:5672) has a client-provided name: ReelForge.WorkflowEngine
rabbitmq         | 2026-03-14 12:15:47.957 | 2026-03-14 11:15:47.955666+00:00 [info] <0.1402.0> connection <0.1402.0> (172.20.0.7:56484 -> 172.20.0.9:5672 - ReelForge.WorkflowEngine): user 'guest' authenticated and granted access to vhost '/'
workflow-engine  | 2026-03-14 12:18:22.741 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:22.742 |       Listing project files for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:22.755 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:22.755 |       Found 108 files for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:24.115 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:24.115 |       Listing project files for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:24.120 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:24.120 |       Found 108 files for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:24.196 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:24.196 |       Listing project files for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:24.198 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:24.198 |       Found 108 files for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:24.246 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:24.246 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference 936fef12-e3c9-491b-a4fd-345cc3e51b76
workflow-engine  | 2026-03-14 12:18:24.426 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:24.426 |       Read project file 936fef12-e3c9-491b-a4fd-345cc3e51b76 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/938aed28-423d-4317-8a46-e30be6fe6ef6/package.json) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:25.059 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:25.059 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference global.css
workflow-engine  | 2026-03-14 12:18:25.083 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:25.083 |       Read project file 5705c9cc-a142-4113-bba5-2855fb35224b (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/a2380f7e-e2a8-4015-8fa1-4ed9c17f3934/global.css) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:25.543 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:25.543 |       Listing project files for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:25.546 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:25.546 |       Found 108 files for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:25.909 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:25.909 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference theme.ts
workflow-engine  | 2026-03-14 12:18:25.922 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:25.922 |       Read project file a1490d39-8444-4273-9265-93d447c8c239 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/c1c04435-c2d8-470f-ab72-71aafccce8dd/theme.ts) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:29.745 | info: ReelForge.WorkflowEngine.Execution.StepExecutors.ParallelStepExecutor[0]
workflow-engine  | 2026-03-14 12:18:29.746 |       Parallel step 1: agent StyleAndThemeExtractor responded with preview: {   "ColorPalette": {     "Primary": "#8b5cf6",     "Secondary": "#d97706",     "Background": "#fafafa",     "Text": null,     "Additional": {       "violetGradientStart": "#8b5cf6",       "violetGradientEnd": "#6d28d9",       "yellowGradientStart": 
inference        | 2026-03-14 12:15:41.753 | warn: Microsoft.AspNetCore.DataProtection.Repositories.FileSystemXmlRepository[60]
inference        | 2026-03-14 12:15:41.753 |       Storing keys in a directory '/home/app/.aspnet/DataProtection-Keys' that may not be persisted outside of the container. Protected data will be unavailable when container is destroyed. For more information go to https://aka.ms/aspnet/dataprotectionwarning
inference        | 2026-03-14 12:15:41.851 | warn: Microsoft.AspNetCore.DataProtection.KeyManagement.XmlKeyManager[35]
inference        | 2026-03-14 12:15:41.851 |       No XML encryptor configured. Key {e85ff61c-0e5a-4853-85a0-d9eb1f45599d} may be persisted to storage in unencrypted form.
inference        | 2026-03-14 12:15:41.887 | info: ReelForge.Inference.Api.Services.Background.FileSummarizationService[0]
inference        | 2026-03-14 12:15:41.887 |       File summarization service started
inference        | 2026-03-14 12:15:42.078 | warn: Microsoft.AspNetCore.Hosting.Diagnostics[15]
inference        | 2026-03-14 12:15:42.078 |       Overriding HTTP_PORTS '8080' and HTTPS_PORTS ''. Binding to values defined by URLS instead 'http://+:8080'.
inference        | 2026-03-14 12:15:42.121 | info: MassTransit[0]
inference        | 2026-03-14 12:15:42.121 |       Bus started: rabbitmq://rabbitmq/
inference        | 2026-03-14 12:15:42.154 | info: Microsoft.Hosting.Lifetime[14]
inference        | 2026-03-14 12:15:42.154 |       Now listening on: http://[::]:8080
inference        | 2026-03-14 12:15:42.154 | info: Microsoft.Hosting.Lifetime[0]
inference        | 2026-03-14 12:15:42.154 |       Application started. Press Ctrl+C to shut down.
inference        | 2026-03-14 12:15:42.154 | info: Microsoft.Hosting.Lifetime[0]
inference        | 2026-03-14 12:15:42.154 |       Hosting environment: Development
inference        | 2026-03-14 12:15:42.154 | info: Microsoft.Hosting.Lifetime[0]
inference        | 2026-03-14 12:15:42.154 |       Content root path: /app
inference        | 2026-03-14 12:15:43.146 | warn: Microsoft.AspNetCore.HttpsPolicy.HttpsRedirectionMiddleware[3]
inference        | 2026-03-14 12:15:43.146 |       Failed to determine the https port for redirect.
inference        | 2026-03-14 12:16:18.753 | fail: Microsoft.AspNetCore.Diagnostics.DeveloperExceptionPageMiddleware[1]
inference        | 2026-03-14 12:16:18.753 |       An unhandled exception has occurred while executing the request.
inference        | 2026-03-14 12:16:18.753 |       Amazon.S3.Model.NoSuchKeyException: The specified key does not exist.
inference        | 2026-03-14 12:16:18.753 |        ---> Amazon.Runtime.Internal.HttpErrorResponseException: Exception of type 'Amazon.Runtime.Internal.HttpErrorResponseException' was thrown.
inference        | 2026-03-14 12:16:18.753 |          at Amazon.Runtime.HttpWebRequestMessage.ProcessHttpResponseMessage(HttpResponseMessage responseMessage)
inference        | 2026-03-14 12:16:18.753 |          at Amazon.Runtime.HttpWebRequestMessage.GetResponseAsync(CancellationToken cancellationToken)
inference        | 2026-03-14 12:16:18.753 |          at Amazon.Runtime.Internal.HttpHandler`1.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:16:18.753 |          at Amazon.Runtime.Internal.RedirectHandler.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:16:18.753 |          at Amazon.Runtime.Internal.Unmarshaller.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:16:18.753 |          at Amazon.S3.Internal.AmazonS3ResponseHandler.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:16:18.753 |          at Amazon.Runtime.Internal.ErrorHandler.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:16:18.753 |          --- End of inner exception stack trace ---
inference        | 2026-03-14 12:16:18.753 |          at Amazon.Runtime.Internal.HttpErrorResponseExceptionHandler.HandleExceptionStream(IRequestContext requestContext, IWebResponseData httpErrorResponse, HttpErrorResponseException exception, Stream responseStream)
inference        | 2026-03-14 12:16:18.753 |          at Amazon.Runtime.Internal.HttpErrorResponseExceptionHandler.HandleExceptionAsync(IExecutionContext executionContext, HttpErrorResponseException exception)
inference        | 2026-03-14 12:16:18.753 |          at Amazon.Runtime.Internal.ExceptionHandler`1.HandleAsync(IExecutionContext executionContext, Exception exception)
inference        | 2026-03-14 12:16:18.753 |          at Amazon.Runtime.Internal.ErrorHandler.ProcessExceptionAsync(IExecutionContext executionContext, Exception exception)
inference        | 2026-03-14 12:16:18.753 |          at Amazon.Runtime.Internal.ErrorHandler.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:16:18.753 |          at Amazon.Runtime.Internal.CallbackHandler.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:16:18.753 |          at Amazon.Runtime.Internal.Signer.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:16:18.753 |          at Amazon.S3.Internal.S3Express.S3ExpressPreSigner.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:16:18.753 |          at Amazon.Runtime.Internal.EndpointDiscoveryHandler.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:16:18.753 |          at Amazon.Runtime.Internal.EndpointDiscoveryHandler.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:16:18.753 |          at Amazon.Runtime.Internal.RetryHandler.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:16:18.753 |          at Amazon.Runtime.Internal.RetryHandler.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:16:18.753 |          at Amazon.Runtime.Internal.CallbackHandler.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:16:18.753 |          at Amazon.Runtime.Internal.BaseAuthResolverHandler.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:16:18.753 |          at Amazon.Runtime.Internal.CallbackHandler.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:16:18.753 |          at Amazon.S3.Internal.AmazonS3ExceptionHandler.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:16:18.753 |          at Amazon.Runtime.Internal.ErrorCallbackHandler.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:16:18.753 |          at Amazon.Runtime.Internal.MetricsHandler.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:16:18.753 |          at ReelForge.Inference.Api.Controllers.OutputsController.DownloadOutput(Guid projectId, Guid stepResultId, CancellationToken ct) in /src/src/ReelForge.Inference.Api/Controllers/OutputsController.cs:line 93
inference        | 2026-03-14 12:16:18.753 |          at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.TaskOfIActionResultExecutor.Execute(ActionContext actionContext, IActionResultTypeMapper mapper, ObjectMethodExecutor executor, Object controller, Object[] arguments)
inference        | 2026-03-14 12:16:18.753 |          at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeActionMethodAsync>g__Awaited|12_0(ControllerActionInvoker invoker, ValueTask`1 actionResultValueTask)
inference        | 2026-03-14 12:16:18.753 |          at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeNextActionFilterAsync>g__Awaited|10_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)
inference        | 2026-03-14 12:16:18.753 |          at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Rethrow(ActionExecutedContextSealed context)
inference        | 2026-03-14 12:16:18.753 |          at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Next(State& next, Scope& scope, Object& state, Boolean& isCompleted)
inference        | 2026-03-14 12:16:18.753 |          at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeInnerFilterAsync>g__Awaited|13_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)
inference        | 2026-03-14 12:16:18.753 |          at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeFilterPipelineAsync>g__Awaited|20_0(ResourceInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)
inference        | 2026-03-14 12:16:18.753 |          at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)
inference        | 2026-03-14 12:16:18.753 |          at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)
inference        | 2026-03-14 12:16:18.753 |          at Microsoft.AspNetCore.Authorization.AuthorizationMiddleware.Invoke(HttpContext context)
inference        | 2026-03-14 12:16:18.753 |          at Microsoft.AspNetCore.Authentication.AuthenticationMiddleware.Invoke(HttpContext context)
inference        | 2026-03-14 12:16:18.753 |          at Swashbuckle.AspNetCore.SwaggerUI.SwaggerUIMiddleware.Invoke(HttpContext httpContext)
inference        | 2026-03-14 12:16:18.753 |          at Swashbuckle.AspNetCore.Swagger.SwaggerMiddleware.Invoke(HttpContext httpContext, ISwaggerProvider swaggerProvider)
inference        | 2026-03-14 12:16:18.753 |          at Microsoft.AspNetCore.Diagnostics.DeveloperExceptionPageMiddlewareImpl.Invoke(HttpContext context)
inference        | 2026-03-14 12:16:23.643 | warn: Microsoft.EntityFrameworkCore.Query[20504]
inference        | 2026-03-14 12:16:23.643 |       Compiling a query which loads related collections for more than one collection navigation, either via 'Include' or through projection, but no 'QuerySplittingBehavior' has been configured. By default, Entity Framework will use 'QuerySplittingBehavior.SingleQuery', which can potentially result in slow query performance. See https://go.microsoft.com/fwlink/?linkid=2134277 for more information. To identify the query that's triggering this warning call 'ConfigureWarnings(w => w.Throw(RelationalEventId.MultipleCollectionIncludeWarning))'.
inference        | 2026-03-14 12:18:20.145 | warn: Microsoft.EntityFrameworkCore.Query[20504]
inference        | 2026-03-14 12:18:20.145 |       Compiling a query which loads related collections for more than one collection navigation, either via 'Include' or through projection, but no 'QuerySplittingBehavior' has been configured. By default, Entity Framework will use 'QuerySplittingBehavior.SingleQuery', which can potentially result in slow query performance. See https://go.microsoft.com/fwlink/?linkid=2134277 for more information. To identify the query that's triggering this warning call 'ConfigureWarnings(w => w.Throw(RelationalEventId.MultipleCollectionIncludeWarning))'.
workflow-engine  | 2026-03-14 12:18:33.382 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.382 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference WorkflowStepList.tsx
workflow-engine  | 2026-03-14 12:18:33.389 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.389 |       Read project file 4daeac02-f4ca-48b6-9c8b-0ff4a4b1c32e (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/25e86607-d39b-4886-9c4c-778830af8259/WorkflowStepList.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.389 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.389 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference StepCard.tsx
workflow-engine  | 2026-03-14 12:18:33.399 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.399 |       Read project file 64dd2d28-947f-4630-9fd1-d879cf2510f1 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/c72cb11e-f7eb-42a6-b61e-84c6b01a9515/StepCard.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.399 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.399 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference StepTypeSelector.tsx
workflow-engine  | 2026-03-14 12:18:33.408 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.408 |       Read project file 6b46dba6-cc5b-433d-a03d-ed5aa93fb761 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/4717b351-6ba3-4593-8a4b-e07b85ca968c/StepTypeSelector.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.408 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.408 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference StepTypeBadge.tsx
workflow-engine  | 2026-03-14 12:18:33.418 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.418 |       Read project file dac9313a-f2bf-4724-9ef2-f488a1c8b3e5 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/b9d433a1-0286-4910-b486-fa5185fc9ddb/StepTypeBadge.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.418 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.418 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference AddStepModal.tsx
workflow-engine  | 2026-03-14 12:18:33.428 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.428 |       Read project file 17346905-7ad3-46bd-8a3d-bc707058a4f4 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/229328b6-9d8c-4a1a-871d-bb0ce1015a9d/AddStepModal.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.428 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.428 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference AgentCard.tsx
workflow-engine  | 2026-03-14 12:18:33.442 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.442 |       Read project file 66598184-c4c6-4d0b-bc1a-65f79806302c (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/04731742-bc7e-42a7-be25-65d8862021ce/AgentCard.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.442 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.442 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference AgentPicker.tsx
workflow-engine  | 2026-03-14 12:18:33.454 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.454 |       Read project file df62823c-a21d-463b-8bb2-584237aa553c (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/23e52b2b-c6fc-4055-921b-7da85b949f51/AgentPicker.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.454 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.454 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference AgentSchemaViewer.tsx
workflow-engine  | 2026-03-14 12:18:33.464 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.464 |       Read project file 119def8b-e369-48ac-8f8d-35eff8ca0322 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/9f88f8a8-2d73-4038-89dc-b7b8c5070f35/AgentSchemaViewer.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.464 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.464 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference AgentForm.tsx
workflow-engine  | 2026-03-14 12:18:33.476 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.476 |       Read project file f387ba66-4501-4996-becb-487d0bf1c41b (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/23a0371f-0d58-47cc-a028-e74e7234917f/AgentForm.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.476 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.476 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference UserMenu.tsx
workflow-engine  | 2026-03-14 12:18:33.487 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.487 |       Read project file 676b0e85-eea3-4f86-bcd7-499f5c7ae2c2 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/36d1702f-58cc-41c5-97f6-2ace290f0e30/UserMenu.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.487 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.487 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ThemeToggle.tsx
workflow-engine  | 2026-03-14 12:18:33.497 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.497 |       Read project file 3666305d-1099-4c7c-a176-0e0778658f7f (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/386c986e-dcdd-4acf-badf-a5ffa4220aa6/ThemeToggle.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.497 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.497 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference FileSummaryDrawer.tsx
workflow-engine  | 2026-03-14 12:18:33.509 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.509 |       Read project file be772ebe-9e5c-4897-b9c0-0aae025d5920 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/35525dad-bfc3-46d3-a951-ecc9f8288bee/FileSummaryDrawer.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.509 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.509 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference FileUploadZone.tsx
workflow-engine  | 2026-03-14 12:18:33.519 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.519 |       Read project file 24ac42ad-17e3-4e34-9796-1c79713bd98f (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/25ed5beb-9160-44ff-9550-37bf224402be/FileUploadZone.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.520 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.520 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference UploadProgressList.tsx
workflow-engine  | 2026-03-14 12:18:33.532 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.532 |       Read project file 8690857e-4744-4e12-a4d8-cf903818046b (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/85c5c774-cc8f-4e62-ba23-c74c203ee54b/UploadProgressList.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.532 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.532 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference FileList.tsx
workflow-engine  | 2026-03-14 12:18:33.543 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.543 |       Read project file 13561fc2-aa0b-4c68-8454-6fbc30271be3 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/e801399d-17a9-4214-83d9-1049a18c281a/FileList.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.543 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.543 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference FlowchartBuilder.tsx
workflow-engine  | 2026-03-14 12:18:33.555 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.555 |       Read project file 1918eb49-5195-4e14-9fea-527b4a10ffce (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/83aaa908-5aac-4393-99db-0720135f6536/FlowchartBuilder.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.557 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.557 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ExecutionProgress.tsx
workflow-engine  | 2026-03-14 12:18:33.573 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.574 |       Read project file 10129a03-185e-45f8-b540-1eb953706eeb (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/b6b1a654-cc08-4df4-a973-b9970004296b/ExecutionProgress.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.574 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.574 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ExecutionHistory.tsx
workflow-engine  | 2026-03-14 12:18:33.585 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.586 |       Read project file 6e580d15-0290-40ff-94ed-34c5d4a7c8a3 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/3a55e05a-e7c4-43fe-b6a7-181bdca2756c/ExecutionHistory.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.586 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.586 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference UserTable.tsx
workflow-engine  | 2026-03-14 12:18:33.598 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.598 |       Read project file af48bd1e-dcad-4095-809b-6307ad6621f5 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/e433d994-66a1-4bcd-92a0-e5edb844d2b2/UserTable.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.598 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.598 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference TempPasswordModal.tsx
workflow-engine  | 2026-03-14 12:18:33.610 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.611 |       Read project file 08ca7950-5411-4a1f-a38a-4b5de41cd8db (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/3c6f9775-dfc1-4acc-ae2a-3f7bdd5008e5/TempPasswordModal.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.611 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.611 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ProjectForm.tsx
workflow-engine  | 2026-03-14 12:18:33.618 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.618 |       Read project file 1bb492a6-c317-4560-91ac-c84f468a9d40 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/a6347e2b-e2e2-4594-bf08-9abfa016e7c9/ProjectForm.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.619 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.619 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ProjectCard.tsx
workflow-engine  | 2026-03-14 12:18:33.629 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.629 |       Read project file 47097ee5-7df6-4018-bc49-7e8c30dd1eda (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/0ed481fb-74b4-44fc-a56f-068931b61e5c/ProjectCard.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.629 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.629 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference PageHeader.tsx
workflow-engine  | 2026-03-14 12:18:33.646 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.646 |       Read project file 9c6a2238-e8e3-4e74-bd2b-350cb9d3109e (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/4b7749fa-b54c-4c8d-ba84-a1f037e7cb9d/PageHeader.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.646 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.646 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ConfirmModal.tsx
workflow-engine  | 2026-03-14 12:18:33.660 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.660 |       Read project file ff38de1c-51f4-48aa-9e68-518322e26d53 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/b6be1637-7c7a-4812-93e6-4316fa4e806b/ConfirmModal.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.660 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.660 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ErrorAlert.tsx
workflow-engine  | 2026-03-14 12:18:33.668 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.668 |       Read project file 6239418c-dbbd-4947-b36f-1852fe02047a (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/a891946f-0a82-4174-aedc-8319c34e2dc9/ErrorAlert.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.668 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.669 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ExecuteWithInputModal.tsx
workflow-engine  | 2026-03-14 12:18:33.678 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.678 |       Read project file a0a04a09-f53e-4288-a93e-a82ed5478f8c (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/e26b3fd0-dfa1-4f03-b7e3-ffa485f32a7b/ExecuteWithInputModal.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.678 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.678 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference NavLinks.tsx
workflow-engine  | 2026-03-14 12:18:33.688 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.689 |       Read project file 259ee957-6186-4a4c-809b-5b29e4b2c849 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/9d183570-7a75-4fcc-b203-9974d6396233/NavLinks.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.689 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.689 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference StatusBadge.tsx
workflow-engine  | 2026-03-14 12:18:33.703 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.703 |       Read project file ff2c87af-7c75-478d-ab7e-c977f35a049b (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/cbe90c3e-8be3-4391-9f36-d3da9f50fe29/StatusBadge.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.703 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.703 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference EmptyState.tsx
workflow-engine  | 2026-03-14 12:18:33.716 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.716 |       Read project file c5c4633a-d7eb-4c50-a439-e846d8d4e469 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/10e8a8d7-a04a-4d6f-9435-a273913bed98/EmptyState.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.716 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.717 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference LoginForm.tsx
workflow-engine  | 2026-03-14 12:18:33.734 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.734 |       Read project file b610f7f0-1beb-47fd-9c44-19e686d934ea (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/91694f1a-c8c0-4618-8433-4efa1cd2038c/LoginForm.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.734 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.734 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference FileSummaryDrawer.tsx
workflow-engine  | 2026-03-14 12:18:33.758 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.758 |       Read project file be772ebe-9e5c-4897-b9c0-0aae025d5920 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/35525dad-bfc3-46d3-a951-ecc9f8288bee/FileSummaryDrawer.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.758 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.758 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference AgentNode.tsx
workflow-engine  | 2026-03-14 12:18:33.770 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.770 |       Read project file 0353135b-f9c5-475a-b223-046cdf54053a (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/3e8d24a2-23e8-49c9-8b11-e70c1ec7ca8d/AgentNode.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.774 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.774 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ReviewLoopNode.tsx
workflow-engine  | 2026-03-14 12:18:33.787 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.787 |       Read project file f5ba5079-1131-46c3-9fa1-3076eb7e4652 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/13cc9907-66fd-4388-95d8-174b64da2428/ReviewLoopNode.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.787 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.787 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ParallelNode.tsx
workflow-engine  | 2026-03-14 12:18:33.798 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.798 |       Read project file d629124b-5c73-4d41-856e-af739c6345ea (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/0b7c7a88-e791-4dd2-9409-8f945e72df7f/ParallelNode.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.798 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.798 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ConditionalNode.tsx
workflow-engine  | 2026-03-14 12:18:33.810 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.811 |       Read project file d1e524c5-ae23-4c15-b4f0-9e19e65c56bf (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/0a021852-c231-4212-9aa0-5880337bb4a5/ConditionalNode.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:33.811 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:33.811 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ConditionalStepConfig.tsx
workflow-engine  | 2026-03-14 12:17:08.850 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:17:08.850 |       Read project file f4b9008e-c744-458b-9e07-e3443ecafdbe (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/e06e2a0b-9906-40e9-8676-2a9e74f34841/ConditionalStepConfig.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:17:08.850 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:17:08.850 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ForEachStepConfig.tsx
workflow-engine  | 2026-03-14 12:17:08.862 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:17:08.862 |       Read project file 8d8d256b-a29c-43f3-aa61-8103f260ee42 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/a1e3907a-4a0a-49a7-b93b-d3a58aa93ee6/ForEachStepConfig.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:17:08.862 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:17:08.862 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference use-projects.ts
workflow-engine  | 2026-03-14 12:17:08.873 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:17:08.873 |       Read project file 48ea3118-58a0-4432-aca2-1b5472afe2a5 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/114e528a-da81-406d-ba14-38c70ec48549/use-projects.ts) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:17:08.873 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:17:08.873 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference use-agents.ts
workflow-engine  | 2026-03-14 12:18:36.586 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:36.587 |       Read project file 7bb8fefb-8a62-4fa0-a8c0-7ad1f990e7aa (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/886f021c-6242-4d33-99c7-3db3ad073ee3/use-agents.ts) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:36.587 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:36.587 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference use-workflows.ts
workflow-engine  | 2026-03-14 12:18:36.596 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:36.596 |       Read project file f97e9dc0-1ae1-4693-9031-645ef68b3511 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/82f2b27f-9002-4d04-aee1-5e595a936b34/use-workflows.ts) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:36.596 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:36.597 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference use-workflow-monitor.ts
workflow-engine  | 2026-03-14 12:18:36.614 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:36.614 |       Read project file 28426595-1b42-4716-8f5b-22a6c7851d78 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/95f83cdc-53cc-4e83-93f7-f59467655229/use-workflow-monitor.ts) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:36.614 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:36.614 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference use-files.ts
workflow-engine  | 2026-03-14 12:18:36.626 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:36.626 |       Read project file 6d139973-0536-4d11-a4b8-bdab610ecc3f (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/90494112-090c-4ee5-bcfe-f544094624ed/use-files.ts) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:36.626 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:36.626 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference use-executions.ts
workflow-engine  | 2026-03-14 12:18:36.635 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:36.635 |       Read project file fd41389f-64e0-49a8-9072-722e00ba911e (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/0abd66d8-0659-418e-b403-a3d2c4ede4d2/use-executions.ts) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:36.635 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:36.635 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference use-outputs.ts
workflow-engine  | 2026-03-14 12:18:36.645 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:36.645 |       Read project file 6d60b23a-e999-40b2-a252-ca7244b8c7d2 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/9adbd5bc-871d-4fe7-84d1-493a5b9a1c25/use-outputs.ts) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:36.645 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:36.645 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference use-workflow-engine.ts
workflow-engine  | 2026-03-14 12:18:36.658 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:36.658 |       Read project file 8bb5354b-c90a-4254-8931-51fa265f5da7 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/77ad050f-da29-4e6a-b698-55a7c276b031/use-workflow-engine.ts) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:36.658 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:36.658 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference use-execution.ts
workflow-engine  | 2026-03-14 12:18:36.670 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:36.670 |       Read project file 9e92c6ca-a477-4a03-a213-cf6b205db0f8 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/df9bc386-78df-4c39-bb79-f53cb3c7b8ff/use-execution.ts) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:36.670 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:36.670 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference use-admin-users.ts
workflow-engine  | 2026-03-14 12:18:36.680 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:36.680 |       Read project file 5af1cef7-e0c2-46a6-b726-8ea5ad5816e9 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/be3450d9-52b7-4084-86b9-b4927e3edaf7/use-admin-users.ts) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:36.680 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:36.680 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference AgentCard.tsx
workflow-engine  | 2026-03-14 12:18:36.693 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:36.693 |       Read project file 66598184-c4c6-4d0b-bc1a-65f79806302c (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/04731742-bc7e-42a7-be25-65d8862021ce/AgentCard.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:36.693 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:36.693 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference AgentTypeBadge.tsx
workflow-engine  | 2026-03-14 12:18:36.704 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:36.704 |       Read project file 3da087ef-c37f-4e08-ab0d-13132470be87 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/daa30444-a853-4520-8892-3e5af9fa3f0b/AgentTypeBadge.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:36.704 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:36.704 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference AgentCapabilityBadge.tsx
workflow-engine  | 2026-03-14 12:18:36.714 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:36.714 |       Read project file 78fe5ce8-84a5-450e-9df6-3cdc91cd7919 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/02f43077-5313-44b0-ace2-6621a768485d/AgentCapabilityBadge.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:36.714 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:36.714 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference AgentForm.tsx
workflow-engine  | 2026-03-14 12:18:36.723 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:36.723 |       Read project file f387ba66-4501-4996-becb-487d0bf1c41b (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/23a0371f-0d58-47cc-a028-e74e7234917f/AgentForm.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:36.723 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:36.723 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference AgentPicker.tsx
workflow-engine  | 2026-03-14 12:18:36.732 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:36.732 |       Read project file df62823c-a21d-463b-8bb2-584237aa553c (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/23e52b2b-c6fc-4055-921b-7da85b949f51/AgentPicker.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:36.732 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:36.732 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference AgentSchemaViewer.tsx
workflow-engine  | 2026-03-14 12:18:36.744 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:36.744 |       Read project file 119def8b-e369-48ac-8f8d-35eff8ca0322 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/9f88f8a8-2d73-4038-89dc-b7b8c5070f35/AgentSchemaViewer.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:36.744 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:36.745 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference AuthLayout.tsx
workflow-engine  | 2026-03-14 12:18:36.751 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:36.751 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference AuthLayout.tsx
workflow-engine  | 2026-03-14 12:18:37.768 | info: ReelForge.WorkflowEngine.Execution.StepExecutors.ParallelStepExecutor[0]
workflow-engine  | 2026-03-14 12:18:37.768 |       Parallel step 1: agent DependencyAnalyzer responded with preview: {   "UI_Dependencies": [     {       "Name": "next",       "Version": "^15.3.0",       "Purpose": "UI framework",       "IsCore": true     },     {       "Name": "react",       "Version": "^19.0.0",       "Purpose": "UI framework",       "IsCore": tr
workflow-engine  | 2026-03-14 12:18:41.334 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.334 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference LoginForm.tsx
workflow-engine  | 2026-03-14 12:18:41.341 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.341 |       Read project file b610f7f0-1beb-47fd-9c44-19e686d934ea (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/91694f1a-c8c0-4618-8433-4efa1cd2038c/LoginForm.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:41.341 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.341 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference UserMenu.tsx
workflow-engine  | 2026-03-14 12:18:41.346 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.346 |       Read project file 676b0e85-eea3-4f86-bcd7-499f5c7ae2c2 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/36d1702f-58cc-41c5-97f6-2ace290f0e30/UserMenu.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:41.346 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.346 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ThemeToggle.tsx
workflow-engine  | 2026-03-14 12:18:41.353 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.354 |       Read project file 3666305d-1099-4c7c-a176-0e0778658f7f (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/386c986e-dcdd-4acf-badf-a5ffa4220aa6/ThemeToggle.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:41.354 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.354 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ConfirmModal.tsx
workflow-engine  | 2026-03-14 12:18:41.360 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.360 |       Read project file ff38de1c-51f4-48aa-9e68-518322e26d53 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/b6be1637-7c7a-4812-93e6-4316fa4e806b/ConfirmModal.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:41.360 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.360 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ErrorAlert.tsx
workflow-engine  | 2026-03-14 12:18:41.367 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.367 |       Read project file 6239418c-dbbd-4947-b36f-1852fe02047a (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/a891946f-0a82-4174-aedc-8319c34e2dc9/ErrorAlert.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:41.367 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.367 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ExecuteWithInputModal.tsx
workflow-engine  | 2026-03-14 12:18:41.374 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.374 |       Read project file a0a04a09-f53e-4288-a93e-a82ed5478f8c (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/e26b3fd0-dfa1-4f03-b7e3-ffa485f32a7b/ExecuteWithInputModal.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:41.374 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.374 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference NavLinks.tsx
workflow-engine  | 2026-03-14 12:18:41.380 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.380 |       Read project file 259ee957-6186-4a4c-809b-5b29e4b2c849 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/9d183570-7a75-4fcc-b203-9974d6396233/NavLinks.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:41.380 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.380 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference StatusBadge.tsx
workflow-engine  | 2026-03-14 12:18:41.385 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.385 |       Read project file ff2c87af-7c75-478d-ab7e-c977f35a049b (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/cbe90c3e-8be3-4391-9f36-d3da9f50fe29/StatusBadge.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:41.385 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.385 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference EmptyState.tsx
workflow-engine  | 2026-03-14 12:18:41.390 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.390 |       Read project file c5c4633a-d7eb-4c50-a439-e846d8d4e469 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/10e8a8d7-a04a-4d6f-9435-a273913bed98/EmptyState.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:41.390 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.390 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ProjectForm.tsx
workflow-engine  | 2026-03-14 12:18:41.398 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.398 |       Read project file 1bb492a6-c317-4560-91ac-c84f468a9d40 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/a6347e2b-e2e2-4594-bf08-9abfa016e7c9/ProjectForm.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:41.398 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.398 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ProjectCard.tsx
workflow-engine  | 2026-03-14 12:18:41.403 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.403 |       Read project file 47097ee5-7df6-4018-bc49-7e8c30dd1eda (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/0ed481fb-74b4-44fc-a56f-068931b61e5c/ProjectCard.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:41.403 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.403 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference PageHeader.tsx
workflow-engine  | 2026-03-14 12:18:41.411 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.411 |       Read project file 9c6a2238-e8e3-4e74-bd2b-350cb9d3109e (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/4b7749fa-b54c-4c8d-ba84-a1f037e7cb9d/PageHeader.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:41.411 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.411 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ExecutionProgress.tsx
workflow-engine  | 2026-03-14 12:18:41.416 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.416 |       Read project file 10129a03-185e-45f8-b540-1eb953706eeb (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/b6b1a654-cc08-4df4-a973-b9970004296b/ExecutionProgress.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:41.416 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.416 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ExecutionHistory.tsx
workflow-engine  | 2026-03-14 12:18:41.422 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.422 |       Read project file 6e580d15-0290-40ff-94ed-34c5d4a7c8a3 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/3a55e05a-e7c4-43fe-b6a7-181bdca2756c/ExecutionHistory.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:41.422 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.422 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference UserTable.tsx
workflow-engine  | 2026-03-14 12:18:41.429 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.429 |       Read project file af48bd1e-dcad-4095-809b-6307ad6621f5 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/e433d994-66a1-4bcd-92a0-e5edb844d2b2/UserTable.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:41.429 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.429 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference TempPasswordModal.tsx
workflow-engine  | 2026-03-14 12:18:41.436 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.436 |       Read project file 08ca7950-5411-4a1f-a38a-4b5de41cd8db (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/3c6f9775-dfc1-4acc-ae2a-3f7bdd5008e5/TempPasswordModal.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:41.436 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.436 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference AgentTypeBadge.tsx
workflow-engine  | 2026-03-14 12:18:41.443 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.443 |       Read project file 3da087ef-c37f-4e08-ab0d-13132470be87 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/daa30444-a853-4520-8892-3e5af9fa3f0b/AgentTypeBadge.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:41.443 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.443 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference AgentCapabilityBadge.tsx
workflow-engine  | 2026-03-14 12:18:41.449 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.449 |       Read project file 78fe5ce8-84a5-450e-9df6-3cdc91cd7919 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/02f43077-5313-44b0-ace2-6621a768485d/AgentCapabilityBadge.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:41.449 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.449 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference AgentForm.tsx
workflow-engine  | 2026-03-14 12:18:41.455 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.455 |       Read project file f387ba66-4501-4996-becb-487d0bf1c41b (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/23a0371f-0d58-47cc-a028-e74e7234917f/AgentForm.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:41.455 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.455 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference AgentCard.tsx
workflow-engine  | 2026-03-14 12:18:41.465 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.465 |       Read project file 66598184-c4c6-4d0b-bc1a-65f79806302c (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/04731742-bc7e-42a7-be25-65d8862021ce/AgentCard.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:41.465 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.465 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference AgentPicker.tsx
workflow-engine  | 2026-03-14 12:18:41.473 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.473 |       Read project file df62823c-a21d-463b-8bb2-584237aa553c (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/23e52b2b-c6fc-4055-921b-7da85b949f51/AgentPicker.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:18:41.473 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.473 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference AgentSchemaViewer.tsx
workflow-engine  | 2026-03-14 12:18:41.479 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:18:41.479 |       Read project file 119def8b-e369-48ac-8f8d-35eff8ca0322 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/9f88f8a8-2d73-4038-89dc-b7b8c5070f35/AgentSchemaViewer.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:04.153 | info: ReelForge.WorkflowEngine.Execution.StepExecutors.ParallelStepExecutor[0]
workflow-engine  | 2026-03-14 12:19:04.153 |       Parallel step 1: agent ComponentInventoryAnalyzer responded with preview: {   "TotalComponents": 58,   "Components": [     {       "Name": "WorkflowStepList",       "FilePath": "WorkflowStepList.tsx",       "Props": [         {           "Name": "steps",           "Type": "StepData[]",           "Required": true,          
workflow-engine  | 2026-03-14 12:19:04.283 | info: ReelForge.WorkflowEngine.Execution.WorkflowExecutorService[0]
workflow-engine  | 2026-03-14 12:19:04.283 |       Step 1 (Parallel) completed for execution 5227a5ab-ab88-4405-8310-819fe90a494b with status Completed; duration 62756ms; tokens 666149; nextStepIndex 1
workflow-engine  | 2026-03-14 12:19:06.031 | info: ReelForge.WorkflowEngine.Execution.WorkflowEventPublisher[0]
workflow-engine  | 2026-03-14 12:19:06.031 |       Publishing step completed event: ExecutionId=5227a5ab-ab88-4405-8310-819fe90a494b, StepId=d0e0486d-3899-4cb4-9bef-4aa3dd2c77f6, Status=Completed
workflow-engine  | 2026-03-14 12:19:11.080 | info: ReelForge.WorkflowEngine.Execution.WorkflowExecutorService[0]
workflow-engine  | 2026-03-14 12:19:11.080 |       Executing step 2 (Parallel) for execution 5227a5ab-ab88-4405-8310-819fe90a494b, attempt 1/3
workflow-engine  | 2026-03-14 12:19:11.095 | info: ReelForge.WorkflowEngine.Execution.StepExecutors.ParallelStepExecutor[0]
workflow-engine  | 2026-03-14 12:19:11.096 |       Parallel step 2: running 2 agents in parallel: Scriptwriter, AnimationStrategy
workflow-engine  | 2026-03-14 12:19:12.473 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:12.474 |       Listing project files for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:12.536 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:12.536 |       Found 108 files for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:12.986 | info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
workflow-engine  | 2026-03-14 12:19:12.986 |       Start processing HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files?*
workflow-engine  | 2026-03-14 12:19:13.028 | info: System.Net.Http.HttpClient.Default.ClientHandler[100]
workflow-engine  | 2026-03-14 12:19:13.028 |       Sending HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files?*
workflow-engine  | 2026-03-14 12:19:16.189 | info: System.Net.Http.HttpClient.Default.ClientHandler[101]
workflow-engine  | 2026-03-14 12:19:16.190 |       Received HTTP response headers after 2648.429ms - 404
workflow-engine  | 2026-03-14 12:19:16.219 | info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
workflow-engine  | 2026-03-14 12:19:16.220 |       End processing HTTP request after 3341.8161ms - 404
workflow-engine  | 2026-03-14 12:19:16.518 | info: ReelForge.WorkflowEngine.Services.RemotionSkills.RemotionSkillsService[0]
workflow-engine  | 2026-03-14 12:19:16.518 |       Loading Remotion skills index from GitHub...
workflow-engine  | 2026-03-14 12:19:16.563 | info: System.Net.Http.HttpClient.RemotionSkills.LogicalHandler[100]
workflow-engine  | 2026-03-14 12:19:16.564 |       Start processing HTTP request GET https://api.github.com/repos/remotion-dev/skills/git/trees/main?*
workflow-engine  | 2026-03-14 12:19:16.564 | info: System.Net.Http.HttpClient.RemotionSkills.ClientHandler[100]
workflow-engine  | 2026-03-14 12:19:16.564 |       Sending HTTP request GET https://api.github.com/repos/remotion-dev/skills/git/trees/main?*
workflow-engine  | 2026-03-14 12:19:18.044 | info: System.Net.Http.HttpClient.RemotionSkills.ClientHandler[101]
workflow-engine  | 2026-03-14 12:19:18.044 |       Received HTTP response headers after 1478.9626ms - 200
workflow-engine  | 2026-03-14 12:19:18.044 | info: System.Net.Http.HttpClient.RemotionSkills.LogicalHandler[101]
workflow-engine  | 2026-03-14 12:19:18.044 |       End processing HTTP request after 1480.2546ms - 200
workflow-engine  | 2026-03-14 12:19:18.146 | info: ReelForge.WorkflowEngine.Services.RemotionSkills.RemotionSkillsService[0]
workflow-engine  | 2026-03-14 12:19:18.146 |       Loaded 38 Remotion skill files
workflow-engine  | 2026-03-14 12:19:18.190 | info: System.Net.Http.HttpClient.RemotionSkills.LogicalHandler[100]
workflow-engine  | 2026-03-14 12:19:18.190 |       Start processing HTTP request GET https://raw.githubusercontent.com/remotion-dev/skills/main/skills/remotion/rules/sequencing.md
workflow-engine  | 2026-03-14 12:19:18.190 | info: System.Net.Http.HttpClient.RemotionSkills.ClientHandler[100]
workflow-engine  | 2026-03-14 12:19:18.190 |       Sending HTTP request GET https://raw.githubusercontent.com/remotion-dev/skills/main/skills/remotion/rules/sequencing.md
workflow-engine  | 2026-03-14 12:19:18.239 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:18.240 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference e5150db8-ca98-4391-b362-c797f35e4da1
workflow-engine  | 2026-03-14 12:19:19.054 | info: System.Net.Http.HttpClient.RemotionSkills.ClientHandler[101]
workflow-engine  | 2026-03-14 12:19:19.054 |       Received HTTP response headers after 864.1003ms - 200
workflow-engine  | 2026-03-14 12:19:19.055 | info: System.Net.Http.HttpClient.RemotionSkills.LogicalHandler[101]
workflow-engine  | 2026-03-14 12:19:19.055 |       End processing HTTP request after 865.3609ms - 200
workflow-engine  | 2026-03-14 12:19:19.076 | info: System.Net.Http.HttpClient.RemotionSkills.LogicalHandler[100]
workflow-engine  | 2026-03-14 12:19:19.076 |       Start processing HTTP request GET https://raw.githubusercontent.com/remotion-dev/skills/main/skills/remotion/rules/transitions.md
workflow-engine  | 2026-03-14 12:19:19.076 | info: System.Net.Http.HttpClient.RemotionSkills.ClientHandler[100]
workflow-engine  | 2026-03-14 12:19:19.076 |       Sending HTTP request GET https://raw.githubusercontent.com/remotion-dev/skills/main/skills/remotion/rules/transitions.md
workflow-engine  | 2026-03-14 12:19:19.317 | info: System.Net.Http.HttpClient.RemotionSkills.ClientHandler[101]
workflow-engine  | 2026-03-14 12:19:19.317 |       Received HTTP response headers after 233.4218ms - 200
workflow-engine  | 2026-03-14 12:19:19.317 | info: System.Net.Http.HttpClient.RemotionSkills.LogicalHandler[101]
workflow-engine  | 2026-03-14 12:19:19.317 |       End processing HTTP request after 234.8223ms - 200
workflow-engine  | 2026-03-14 12:19:19.370 | info: System.Net.Http.HttpClient.RemotionSkills.LogicalHandler[100]
workflow-engine  | 2026-03-14 12:19:19.370 |       Start processing HTTP request GET https://raw.githubusercontent.com/remotion-dev/skills/main/skills/remotion/rules/timing.md
workflow-engine  | 2026-03-14 12:19:19.370 | info: System.Net.Http.HttpClient.RemotionSkills.ClientHandler[100]
workflow-engine  | 2026-03-14 12:19:19.370 |       Sending HTTP request GET https://raw.githubusercontent.com/remotion-dev/skills/main/skills/remotion/rules/timing.md
workflow-engine  | 2026-03-14 12:19:19.820 | info: System.Net.Http.HttpClient.RemotionSkills.ClientHandler[101]
workflow-engine  | 2026-03-14 12:19:19.820 |       Received HTTP response headers after 449.5387ms - 200
workflow-engine  | 2026-03-14 12:19:19.821 | info: System.Net.Http.HttpClient.RemotionSkills.LogicalHandler[101]
workflow-engine  | 2026-03-14 12:19:19.821 |       End processing HTTP request after 461.2427ms - 200
workflow-engine  | 2026-03-14 12:19:20.752 | info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
workflow-engine  | 2026-03-14 12:19:20.752 |       Start processing HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files?*
workflow-engine  | 2026-03-14 12:19:20.752 | info: System.Net.Http.HttpClient.Default.ClientHandler[100]
workflow-engine  | 2026-03-14 12:19:20.752 |       Sending HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files?*
workflow-engine  | 2026-03-14 12:19:20.760 | info: System.Net.Http.HttpClient.Default.ClientHandler[101]
workflow-engine  | 2026-03-14 12:19:20.761 |       Received HTTP response headers after 7.8674ms - 404
workflow-engine  | 2026-03-14 12:19:20.761 | info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
workflow-engine  | 2026-03-14 12:19:20.761 |       End processing HTTP request after 12.2596ms - 404
workflow-engine  | 2026-03-14 12:19:21.790 | info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
workflow-engine  | 2026-03-14 12:19:21.790 |       Start processing HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files?*
workflow-engine  | 2026-03-14 12:19:21.790 | info: System.Net.Http.HttpClient.Default.ClientHandler[100]
workflow-engine  | 2026-03-14 12:19:21.790 |       Sending HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files?*
workflow-engine  | 2026-03-14 12:19:21.800 | info: System.Net.Http.HttpClient.Default.ClientHandler[101]
workflow-engine  | 2026-03-14 12:19:21.802 |       Received HTTP response headers after 20.0172ms - 404
workflow-engine  | 2026-03-14 12:19:21.803 | info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
workflow-engine  | 2026-03-14 12:19:21.803 |       End processing HTTP request after 26.6735ms - 404
workflow-engine  | 2026-03-14 12:19:22.779 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:22.779 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference dd850550-f865-4245-b067-48c492766058
workflow-engine  | 2026-03-14 12:19:25.670 | info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
workflow-engine  | 2026-03-14 12:19:25.670 |       Start processing HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files?*
workflow-engine  | 2026-03-14 12:19:25.670 | info: System.Net.Http.HttpClient.Default.ClientHandler[100]
workflow-engine  | 2026-03-14 12:19:25.670 |       Sending HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files?*
workflow-engine  | 2026-03-14 12:19:25.704 | info: System.Net.Http.HttpClient.Default.ClientHandler[101]
workflow-engine  | 2026-03-14 12:19:25.704 |       Received HTTP response headers after 29.2774ms - 404
workflow-engine  | 2026-03-14 12:19:25.704 | info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
workflow-engine  | 2026-03-14 12:19:25.704 |       End processing HTTP request after 39.0175ms - 404
workflow-engine  | 2026-03-14 12:19:25.938 | fail: ReelForge.WorkflowEngine.Execution.WorkflowExecutorService[0]
workflow-engine  | 2026-03-14 12:19:25.939 |       Workflow execution 5227a5ab-ab88-4405-8310-819fe90a494b failed
workflow-engine  | 2026-03-14 12:19:25.939 |       System.InvalidOperationException: Sandbox API request failed (404): sandbox not found
workflow-engine  | 2026-03-14 12:19:25.939 |          at ReelForge.WorkflowEngine.Agents.Tools.ReactRemotionSandboxTools.ReadJsonAsync[T](HttpResponseMessage response) in /src/src/ReelForge.WorkflowEngine/Agents/Tools/ReactRemotionSandboxTools.cs:line 558
workflow-engine  | 2026-03-14 12:19:25.939 |          at ReelForge.WorkflowEngine.Agents.Tools.ReactRemotionSandboxTools.ListSandboxFiles(String path) in /src/src/ReelForge.WorkflowEngine/Agents/Tools/ReactRemotionSandboxTools.cs:line 297
workflow-engine  | 2026-03-14 12:19:25.939 |          at Microsoft.Extensions.AI.AIFunctionFactory.ReflectionAIFunctionDescriptor.<>c__DisplayClass35_1.<<GetReturnParameterMarshaller>b__12>d.MoveNext()
workflow-engine  | 2026-03-14 12:19:25.939 |       --- End of stack trace from previous location ---
workflow-engine  | 2026-03-14 12:19:25.939 |          at Microsoft.Extensions.AI.AIFunctionFactory.ReflectionAIFunction.InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
workflow-engine  | 2026-03-14 12:19:25.939 |          at Microsoft.Extensions.AI.AIFunctionFactory.ReflectionAIFunction.InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
workflow-engine  | 2026-03-14 12:19:25.939 |          at Microsoft.Extensions.AI.FunctionInvokingChatClient.InstrumentedInvokeFunctionAsync(FunctionInvocationContext context, CancellationToken cancellationToken)
workflow-engine  | 2026-03-14 12:19:25.939 |          at Microsoft.Extensions.AI.FunctionInvokingChatClient.ProcessFunctionCallAsync(List`1 messages, ChatOptions options, List`1 callContents, Int32 iteration, Int32 functionCallIndex, Boolean captureExceptions, Boolean isStreaming, CancellationToken cancellationToken)
workflow-engine  | 2026-03-14 12:19:25.939 |          at Microsoft.Extensions.AI.FunctionInvokingChatClient.ProcessFunctionCallsAsync(List`1 messages, ChatOptions options, List`1 functionCallContents, Int32 iteration, Int32 consecutiveErrorCount, Boolean isStreaming, CancellationToken cancellationToken)
workflow-engine  | 2026-03-14 12:19:25.939 |          at Microsoft.Extensions.AI.FunctionInvokingChatClient.GetResponseAsync(IEnumerable`1 messages, ChatOptions options, CancellationToken cancellationToken)
workflow-engine  | 2026-03-14 12:19:25.939 |          at Microsoft.Agents.AI.ChatClientAgent.RunCoreAsync(IEnumerable`1 messages, AgentSession session, AgentRunOptions options, CancellationToken cancellationToken)
workflow-engine  | 2026-03-14 12:19:25.939 |          at Microsoft.Agents.AI.ChatClientAgent.RunCoreAsync(IEnumerable`1 messages, AgentSession session, AgentRunOptions options, CancellationToken cancellationToken)
workflow-engine  | 2026-03-14 12:19:25.939 |          at ReelForge.WorkflowEngine.Agents.ReelForgeAgentBase.RunAsync(String prompt, CancellationToken ct) in /src/src/ReelForge.WorkflowEngine/Agents/ReelForgeAgentBase.cs:line 96
workflow-engine  | 2026-03-14 12:19:25.939 |          at ReelForge.WorkflowEngine.Execution.StepExecutors.ParallelStepExecutor.<>c__DisplayClass7_0.<<ExecuteAsync>b__3>d.MoveNext() in /src/src/ReelForge.WorkflowEngine/Execution/StepExecutors/ParallelStepExecutor.cs:line 118
workflow-engine  | 2026-03-14 12:19:25.939 |       --- End of stack trace from previous location ---
workflow-engine  | 2026-03-14 12:19:25.939 |          at System.Threading.Tasks.Parallel.<>c__53`1.<<ForEachAsync>b__53_0>d.MoveNext()
workflow-engine  | 2026-03-14 12:19:25.939 |       --- End of stack trace from previous location ---
workflow-engine  | 2026-03-14 12:19:25.939 |          at ReelForge.WorkflowEngine.Execution.StepExecutors.ParallelStepExecutor.ExecuteAsync(StepExecutionContext context) in /src/src/ReelForge.WorkflowEngine/Execution/StepExecutors/ParallelStepExecutor.cs:line 111
workflow-engine  | 2026-03-14 12:19:25.939 |          at ReelForge.WorkflowEngine.Execution.WorkflowExecutorService.ExecuteStepWithRetryAsync(IStepExecutor executor, StepExecutionContext context, WorkflowStep step, CancellationToken ct) in /src/src/ReelForge.WorkflowEngine/Execution/WorkflowExecutorService.cs:line 424
workflow-engine  | 2026-03-14 12:19:25.939 |          at ReelForge.WorkflowEngine.Execution.WorkflowExecutorService.ExecuteAsync(Guid executionId, String correlationId, CancellationToken ct) in /src/src/ReelForge.WorkflowEngine/Execution/WorkflowExecutorService.cs:line 141
workflow-engine  | 2026-03-14 12:19:26.026 | info: ReelForge.WorkflowEngine.Execution.WorkflowEventPublisher[0]
workflow-engine  | 2026-03-14 12:19:26.026 |       Publishing execution failed event: ExecutionId=5227a5ab-ab88-4405-8310-819fe90a494b, ErrorMessage=Sandbox API request failed (404): sandbox not found
workflow-engine  | 2026-03-14 12:19:26.396 | fail: ReelForge.WorkflowEngine.Consumers.WorkflowExecutionRequestedConsumer[0]
workflow-engine  | 2026-03-14 12:19:26.396 |       Workflow execution 5227a5ab-ab88-4405-8310-819fe90a494b failed
workflow-engine  | 2026-03-14 12:19:26.397 |       System.InvalidOperationException: Sandbox API request failed (404): sandbox not found
workflow-engine  | 2026-03-14 12:19:26.397 |          at ReelForge.WorkflowEngine.Agents.Tools.ReactRemotionSandboxTools.ReadJsonAsync[T](HttpResponseMessage response) in /src/src/ReelForge.WorkflowEngine/Agents/Tools/ReactRemotionSandboxTools.cs:line 558
workflow-engine  | 2026-03-14 12:19:26.397 |          at ReelForge.WorkflowEngine.Agents.Tools.ReactRemotionSandboxTools.ListSandboxFiles(String path) in /src/src/ReelForge.WorkflowEngine/Agents/Tools/ReactRemotionSandboxTools.cs:line 297
workflow-engine  | 2026-03-14 12:19:26.397 |          at Microsoft.Extensions.AI.AIFunctionFactory.ReflectionAIFunctionDescriptor.<>c__DisplayClass35_1.<<GetReturnParameterMarshaller>b__12>d.MoveNext()
workflow-engine  | 2026-03-14 12:19:26.397 |       --- End of stack trace from previous location ---
workflow-engine  | 2026-03-14 12:19:26.397 |          at Microsoft.Extensions.AI.AIFunctionFactory.ReflectionAIFunction.InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
workflow-engine  | 2026-03-14 12:19:26.397 |          at Microsoft.Extensions.AI.AIFunctionFactory.ReflectionAIFunction.InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
workflow-engine  | 2026-03-14 12:19:26.397 |          at Microsoft.Extensions.AI.FunctionInvokingChatClient.InstrumentedInvokeFunctionAsync(FunctionInvocationContext context, CancellationToken cancellationToken)
workflow-engine  | 2026-03-14 12:19:26.397 |          at Microsoft.Extensions.AI.FunctionInvokingChatClient.ProcessFunctionCallAsync(List`1 messages, ChatOptions options, List`1 callContents, Int32 iteration, Int32 functionCallIndex, Boolean captureExceptions, Boolean isStreaming, CancellationToken cancellationToken)
workflow-engine  | 2026-03-14 12:19:26.397 |          at Microsoft.Extensions.AI.FunctionInvokingChatClient.ProcessFunctionCallsAsync(List`1 messages, ChatOptions options, List`1 functionCallContents, Int32 iteration, Int32 consecutiveErrorCount, Boolean isStreaming, CancellationToken cancellationToken)
workflow-engine  | 2026-03-14 12:19:26.397 |          at Microsoft.Extensions.AI.FunctionInvokingChatClient.GetResponseAsync(IEnumerable`1 messages, ChatOptions options, CancellationToken cancellationToken)
workflow-engine  | 2026-03-14 12:19:26.397 |          at Microsoft.Agents.AI.ChatClientAgent.RunCoreAsync(IEnumerable`1 messages, AgentSession session, AgentRunOptions options, CancellationToken cancellationToken)
workflow-engine  | 2026-03-14 12:19:26.397 |          at Microsoft.Agents.AI.ChatClientAgent.RunCoreAsync(IEnumerable`1 messages, AgentSession session, AgentRunOptions options, CancellationToken cancellationToken)
workflow-engine  | 2026-03-14 12:19:26.397 |          at ReelForge.WorkflowEngine.Agents.ReelForgeAgentBase.RunAsync(String prompt, CancellationToken ct) in /src/src/ReelForge.WorkflowEngine/Agents/ReelForgeAgentBase.cs:line 96
workflow-engine  | 2026-03-14 12:19:26.397 |          at ReelForge.WorkflowEngine.Execution.StepExecutors.ParallelStepExecutor.<>c__DisplayClass7_0.<<ExecuteAsync>b__3>d.MoveNext() in /src/src/ReelForge.WorkflowEngine/Execution/StepExecutors/ParallelStepExecutor.cs:line 118
workflow-engine  | 2026-03-14 12:19:26.397 |       --- End of stack trace from previous location ---
workflow-engine  | 2026-03-14 12:19:26.397 |          at System.Threading.Tasks.Parallel.<>c__53`1.<<ForEachAsync>b__53_0>d.MoveNext()
workflow-engine  | 2026-03-14 12:19:26.397 |       --- End of stack trace from previous location ---
workflow-engine  | 2026-03-14 12:19:26.397 |          at ReelForge.WorkflowEngine.Execution.StepExecutors.ParallelStepExecutor.ExecuteAsync(StepExecutionContext context) in /src/src/ReelForge.WorkflowEngine/Execution/StepExecutors/ParallelStepExecutor.cs:line 111
workflow-engine  | 2026-03-14 12:19:26.397 |          at ReelForge.WorkflowEngine.Execution.WorkflowExecutorService.ExecuteStepWithRetryAsync(IStepExecutor executor, StepExecutionContext context, WorkflowStep step, CancellationToken ct) in /src/src/ReelForge.WorkflowEngine/Execution/WorkflowExecutorService.cs:line 424
workflow-engine  | 2026-03-14 12:19:26.397 |          at ReelForge.WorkflowEngine.Execution.WorkflowExecutorService.ExecuteAsync(Guid executionId, String correlationId, CancellationToken ct) in /src/src/ReelForge.WorkflowEngine/Execution/WorkflowExecutorService.cs:line 141
workflow-engine  | 2026-03-14 12:19:26.397 |          at ReelForge.WorkflowEngine.Execution.WorkflowExecutorService.ExecuteAsync(Guid executionId, String correlationId, CancellationToken ct) in /src/src/ReelForge.WorkflowEngine/Execution/WorkflowExecutorService.cs:line 309
workflow-engine  | 2026-03-14 12:19:26.397 |          at ReelForge.WorkflowEngine.Consumers.WorkflowExecutionRequestedConsumer.Consume(ConsumeContext`1 context) in /src/src/ReelForge.WorkflowEngine/Consumers/WorkflowExecutionRequestedConsumer.cs:line 32
workflow-engine  | 2026-03-14 12:19:27.126 | warn: MassTransit.ReceiveTransport[0]
workflow-engine  | 2026-03-14 12:19:27.127 |       R-RETRY rabbitmq://rabbitmq/workflow-execution 01000000-06bd-0e5c-0c68-08de81bb65c6 MassTransit.RetryPolicies.RetryConsumeContext<ReelForge.Shared.IntegrationEvents.WorkflowExecutionRequested>
workflow-engine  | 2026-03-14 12:19:27.127 |       System.InvalidOperationException: Sandbox API request failed (404): sandbox not found
workflow-engine  | 2026-03-14 12:19:27.127 |          at ReelForge.WorkflowEngine.Agents.Tools.ReactRemotionSandboxTools.ReadJsonAsync[T](HttpResponseMessage response) in /src/src/ReelForge.WorkflowEngine/Agents/Tools/ReactRemotionSandboxTools.cs:line 558
workflow-engine  | 2026-03-14 12:19:27.127 |          at ReelForge.WorkflowEngine.Agents.Tools.ReactRemotionSandboxTools.ListSandboxFiles(String path) in /src/src/ReelForge.WorkflowEngine/Agents/Tools/ReactRemotionSandboxTools.cs:line 297
workflow-engine  | 2026-03-14 12:19:27.127 |          at Microsoft.Extensions.AI.AIFunctionFactory.ReflectionAIFunctionDescriptor.<>c__DisplayClass35_1.<<GetReturnParameterMarshaller>b__12>d.MoveNext()
workflow-engine  | 2026-03-14 12:19:27.127 |       --- End of stack trace from previous location ---
workflow-engine  | 2026-03-14 12:19:27.127 |          at Microsoft.Extensions.AI.AIFunctionFactory.ReflectionAIFunction.InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
workflow-engine  | 2026-03-14 12:19:27.127 |          at Microsoft.Extensions.AI.AIFunctionFactory.ReflectionAIFunction.InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
workflow-engine  | 2026-03-14 12:19:27.127 |          at Microsoft.Extensions.AI.FunctionInvokingChatClient.InstrumentedInvokeFunctionAsync(FunctionInvocationContext context, CancellationToken cancellationToken)
workflow-engine  | 2026-03-14 12:19:27.127 |          at Microsoft.Extensions.AI.FunctionInvokingChatClient.ProcessFunctionCallAsync(List`1 messages, ChatOptions options, List`1 callContents, Int32 iteration, Int32 functionCallIndex, Boolean captureExceptions, Boolean isStreaming, CancellationToken cancellationToken)
workflow-engine  | 2026-03-14 12:19:27.127 |          at Microsoft.Extensions.AI.FunctionInvokingChatClient.ProcessFunctionCallsAsync(List`1 messages, ChatOptions options, List`1 functionCallContents, Int32 iteration, Int32 consecutiveErrorCount, Boolean isStreaming, CancellationToken cancellationToken)
workflow-engine  | 2026-03-14 12:19:27.127 |          at Microsoft.Extensions.AI.FunctionInvokingChatClient.GetResponseAsync(IEnumerable`1 messages, ChatOptions options, CancellationToken cancellationToken)
workflow-engine  | 2026-03-14 12:19:27.127 |          at Microsoft.Agents.AI.ChatClientAgent.RunCoreAsync(IEnumerable`1 messages, AgentSession session, AgentRunOptions options, CancellationToken cancellationToken)
workflow-engine  | 2026-03-14 12:19:27.127 |          at Microsoft.Agents.AI.ChatClientAgent.RunCoreAsync(IEnumerable`1 messages, AgentSession session, AgentRunOptions options, CancellationToken cancellationToken)
workflow-engine  | 2026-03-14 12:19:27.127 |          at ReelForge.WorkflowEngine.Agents.ReelForgeAgentBase.RunAsync(String prompt, CancellationToken ct) in /src/src/ReelForge.WorkflowEngine/Agents/ReelForgeAgentBase.cs:line 96
workflow-engine  | 2026-03-14 12:19:27.127 |          at ReelForge.WorkflowEngine.Execution.StepExecutors.ParallelStepExecutor.<>c__DisplayClass7_0.<<ExecuteAsync>b__3>d.MoveNext() in /src/src/ReelForge.WorkflowEngine/Execution/StepExecutors/ParallelStepExecutor.cs:line 118
workflow-engine  | 2026-03-14 12:19:27.127 |       --- End of stack trace from previous location ---
workflow-engine  | 2026-03-14 12:19:27.127 |          at System.Threading.Tasks.Parallel.<>c__53`1.<<ForEachAsync>b__53_0>d.MoveNext()
workflow-engine  | 2026-03-14 12:19:27.127 |       --- End of stack trace from previous location ---
workflow-engine  | 2026-03-14 12:19:27.127 |          at ReelForge.WorkflowEngine.Execution.StepExecutors.ParallelStepExecutor.ExecuteAsync(StepExecutionContext context) in /src/src/ReelForge.WorkflowEngine/Execution/StepExecutors/ParallelStepExecutor.cs:line 111
workflow-engine  | 2026-03-14 12:19:27.128 |          at ReelForge.WorkflowEngine.Execution.WorkflowExecutorService.ExecuteStepWithRetryAsync(IStepExecutor executor, StepExecutionContext context, WorkflowStep step, CancellationToken ct) in /src/src/ReelForge.WorkflowEngine/Execution/WorkflowExecutorService.cs:line 424
workflow-engine  | 2026-03-14 12:19:27.128 |          at ReelForge.WorkflowEngine.Execution.WorkflowExecutorService.ExecuteAsync(Guid executionId, String correlationId, CancellationToken ct) in /src/src/ReelForge.WorkflowEngine/Execution/WorkflowExecutorService.cs:line 141
workflow-engine  | 2026-03-14 12:19:27.128 |          at ReelForge.WorkflowEngine.Execution.WorkflowExecutorService.ExecuteAsync(Guid executionId, String correlationId, CancellationToken ct) in /src/src/ReelForge.WorkflowEngine/Execution/WorkflowExecutorService.cs:line 309
workflow-engine  | 2026-03-14 12:19:27.128 |          at ReelForge.WorkflowEngine.Consumers.WorkflowExecutionRequestedConsumer.Consume(ConsumeContext`1 context) in /src/src/ReelForge.WorkflowEngine/Consumers/WorkflowExecutionRequestedConsumer.cs:line 32
workflow-engine  | 2026-03-14 12:19:27.128 |          at MassTransit.DependencyInjection.ScopeConsumerFactory`1.Send[TMessage](ConsumeContext`1 context, IPipe`1 next) in /_/src/MassTransit/DependencyInjection/DependencyInjection/ScopeConsumerFactory.cs:line 22
workflow-engine  | 2026-03-14 12:19:27.128 |          at MassTransit.DependencyInjection.ScopeConsumerFactory`1.Send[TMessage](ConsumeContext`1 context, IPipe`1 next) in /_/src/MassTransit/DependencyInjection/DependencyInjection/ScopeConsumerFactory.cs:line 22
workflow-engine  | 2026-03-14 12:19:27.128 |          at MassTransit.Middleware.ConsumerMessageFilter`2.MassTransit.IFilter<MassTransit.ConsumeContext<TMessage>>.Send(ConsumeContext`1 context, IPipe`1 next) in /_/src/MassTransit/Middleware/ConsumerMessageFilter.cs:line 48
workflow-engine  | 2026-03-14 12:19:27.128 |          at MassTransit.Middleware.ConsumerMessageFilter`2.MassTransit.IFilter<MassTransit.ConsumeContext<TMessage>>.Send(ConsumeContext`1 context, IPipe`1 next) in /_/src/MassTransit/Middleware/ConsumerMessageFilter.cs:line 73
workflow-engine  | 2026-03-14 12:19:27.128 |          at MassTransit.Middleware.RetryFilter`1.MassTransit.IFilter<TContext>.Send(TContext context, IPipe`1 next) in /_/src/MassTransit/Middleware/RetryFilter.cs:line 47
workflow-engine  | 2026-03-14 12:19:32.347 | info: ReelForge.WorkflowEngine.Consumers.WorkflowExecutionRequestedConsumer[0]
workflow-engine  | 2026-03-14 12:19:32.348 |       Received workflow execution request: ExecutionId=5227a5ab-ab88-4405-8310-819fe90a494b, CorrelationId=cde0617e-63c6-45ea-b469-42adc569e692
workflow-engine  | 2026-03-14 12:19:32.524 | info: ReelForge.WorkflowEngine.Execution.WorkflowExecutorService[0]
workflow-engine  | 2026-03-14 12:19:32.524 |       Starting workflow execution 5227a5ab-ab88-4405-8310-819fe90a494b for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 with 3 steps (CorrelationId=cde0617e-63c6-45ea-b469-42adc569e692)
workflow-engine  | 2026-03-14 12:19:32.576 | info: ReelForge.WorkflowEngine.Execution.WorkflowExecutorService[0]
workflow-engine  | 2026-03-14 12:19:32.576 |       Executing step 1 (Parallel) for execution 5227a5ab-ab88-4405-8310-819fe90a494b, attempt 1/3
workflow-engine  | 2026-03-14 12:19:32.610 | info: ReelForge.WorkflowEngine.Execution.StepExecutors.ParallelStepExecutor[0]
workflow-engine  | 2026-03-14 12:19:32.610 |       Parallel step 1: running 3 agents in parallel: ComponentInventoryAnalyzer, DependencyAnalyzer, StyleAndThemeExtractor
workflow-engine  | 2026-03-14 12:19:33.076 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:33.076 |       Listing project files for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:33.136 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:33.137 |       Found 108 files for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:33.137 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:33.137 |       Listing project files for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:33.205 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:33.206 |       Found 108 files for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:33.913 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:33.914 |       Listing project files for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:34.094 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:34.094 |       Found 108 files for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:34.266 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:34.266 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference global.css
workflow-engine  | 2026-03-14 12:19:34.412 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:34.412 |       Read project file 5705c9cc-a142-4113-bba5-2855fb35224b (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/a2380f7e-e2a8-4015-8fa1-4ed9c17f3934/global.css) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:35.028 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:35.028 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference 936fef12-e3c9-491b-a4fd-345cc3e51b76
workflow-engine  | 2026-03-14 12:19:35.160 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:35.160 |       Read project file 936fef12-e3c9-491b-a4fd-345cc3e51b76 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/938aed28-423d-4317-8a46-e30be6fe6ef6/package.json) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:35.811 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:35.812 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference theme.ts
workflow-engine  | 2026-03-14 12:19:35.901 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:35.901 |       Read project file a1490d39-8444-4273-9265-93d447c8c239 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/c1c04435-c2d8-470f-ab72-71aafccce8dd/theme.ts) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:36.170 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:36.170 |       Listing project files for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:36.253 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:36.253 |       Found 108 files for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:45.323 | info: ReelForge.WorkflowEngine.Execution.StepExecutors.ParallelStepExecutor[0]
workflow-engine  | 2026-03-14 12:19:45.323 |       Parallel step 1: agent StyleAndThemeExtractor responded with preview: {   "ColorPalette": {     "Primary": "#8b5cf6",     "Secondary": null,     "Background": "#fafafa",     "Text": null,     "Additional": {       "violetGradient": "linear-gradient(135deg, #8b5cf6 0%, #6d28d9 100%)",       "yellowGradient": "linear-gra
workflow-engine  | 2026-03-14 12:19:46.645 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:46.645 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference WorkflowStepList.tsx
workflow-engine  | 2026-03-14 12:19:46.766 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:46.766 |       Read project file 4daeac02-f4ca-48b6-9c8b-0ff4a4b1c32e (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/25e86607-d39b-4886-9c4c-778830af8259/WorkflowStepList.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:46.781 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:46.781 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference StepCard.tsx
workflow-engine  | 2026-03-14 12:19:46.995 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:46.995 |       Read project file 64dd2d28-947f-4630-9fd1-d879cf2510f1 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/c72cb11e-f7eb-42a6-b61e-84c6b01a9515/StepCard.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:46.996 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:46.996 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference StepTypeSelector.tsx
workflow-engine  | 2026-03-14 12:19:47.138 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:47.138 |       Read project file 6b46dba6-cc5b-433d-a03d-ed5aa93fb761 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/4717b351-6ba3-4593-8a4b-e07b85ca968c/StepTypeSelector.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:47.138 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:47.138 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference StepTypeBadge.tsx
workflow-engine  | 2026-03-14 12:19:47.395 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:47.395 |       Read project file dac9313a-f2bf-4724-9ef2-f488a1c8b3e5 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/b9d433a1-0286-4910-b486-fa5185fc9ddb/StepTypeBadge.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:47.396 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:47.396 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference AddStepModal.tsx
workflow-engine  | 2026-03-14 12:19:47.525 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:47.527 |       Read project file 17346905-7ad3-46bd-8a3d-bc707058a4f4 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/229328b6-9d8c-4a1a-871d-bb0ce1015a9d/AddStepModal.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:47.528 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:47.528 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference FileList.tsx
workflow-engine  | 2026-03-14 12:19:47.674 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:47.687 |       Read project file 13561fc2-aa0b-4c68-8454-6fbc30271be3 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/e801399d-17a9-4214-83d9-1049a18c281a/FileList.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:47.688 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:47.689 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference FileSummaryDrawer.tsx
workflow-engine  | 2026-03-14 12:19:47.837 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:47.837 |       Read project file be772ebe-9e5c-4897-b9c0-0aae025d5920 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/35525dad-bfc3-46d3-a951-ecc9f8288bee/FileSummaryDrawer.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:47.839 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:47.839 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference FileUploadZone.tsx
workflow-engine  | 2026-03-14 12:19:47.990 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:47.990 |       Read project file 24ac42ad-17e3-4e34-9796-1c79713bd98f (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/25ed5beb-9160-44ff-9550-37bf224402be/FileUploadZone.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:47.991 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:47.991 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference UploadProgressList.tsx
workflow-engine  | 2026-03-14 12:19:48.162 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:48.162 |       Read project file 8690857e-4744-4e12-a4d8-cf903818046b (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/85c5c774-cc8f-4e62-ba23-c74c203ee54b/UploadProgressList.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:48.163 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:48.163 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ExecutionProgress.tsx
workflow-engine  | 2026-03-14 12:19:48.286 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:48.286 |       Read project file 10129a03-185e-45f8-b540-1eb953706eeb (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/b6b1a654-cc08-4df4-a973-b9970004296b/ExecutionProgress.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:48.286 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:48.286 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ExecutionHistory.tsx
workflow-engine  | 2026-03-14 12:19:48.480 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:48.481 |       Read project file 6e580d15-0290-40ff-94ed-34c5d4a7c8a3 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/3a55e05a-e7c4-43fe-b6a7-181bdca2756c/ExecutionHistory.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:48.511 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:48.511 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference UserMenu.tsx
workflow-engine  | 2026-03-14 12:19:48.690 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:48.691 |       Read project file 676b0e85-eea3-4f86-bcd7-499f5c7ae2c2 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/36d1702f-58cc-41c5-97f6-2ace290f0e30/UserMenu.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:48.693 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:48.694 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference AgentPicker.tsx
workflow-engine  | 2026-03-14 12:19:48.835 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:48.835 |       Read project file df62823c-a21d-463b-8bb2-584237aa553c (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/23e52b2b-c6fc-4055-921b-7da85b949f51/AgentPicker.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:48.884 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:48.885 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference AgentCard.tsx
workflow-engine  | 2026-03-14 12:19:49.036 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:49.036 |       Read project file 66598184-c4c6-4d0b-bc1a-65f79806302c (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/04731742-bc7e-42a7-be25-65d8862021ce/AgentCard.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:49.036 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:49.036 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference AgentTypeBadge.tsx
workflow-engine  | 2026-03-14 12:19:49.293 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:49.294 |       Read project file 3da087ef-c37f-4e08-ab0d-13132470be87 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/daa30444-a853-4520-8892-3e5af9fa3f0b/AgentTypeBadge.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:49.294 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:49.294 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference AgentCapabilityBadge.tsx
workflow-engine  | 2026-03-14 12:19:49.651 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:49.653 |       Read project file 78fe5ce8-84a5-450e-9df6-3cdc91cd7919 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/02f43077-5313-44b0-ace2-6621a768485d/AgentCapabilityBadge.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:49.656 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:49.656 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference AgentForm.tsx
workflow-engine  | 2026-03-14 12:19:50.281 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:50.281 |       Read project file f387ba66-4501-4996-becb-487d0bf1c41b (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/23a0371f-0d58-47cc-a028-e74e7234917f/AgentForm.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:50.281 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:50.281 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference AgentSchemaViewer.tsx
workflow-engine  | 2026-03-14 12:19:51.062 | info: ReelForge.WorkflowEngine.Execution.StepExecutors.ParallelStepExecutor[0]
workflow-engine  | 2026-03-14 12:19:51.063 |       Parallel step 1: agent DependencyAnalyzer responded with preview: {   "UIFrameworks": [     {       "Name": "next",       "Version": "^15.3.0",       "Purpose": "UI framework",       "IsCore": true     },     {       "Name": "react",       "Version": "^19.0.0",       "Purpose": "UI framework",       "IsCore": true 
workflow-engine  | 2026-03-14 12:19:51.321 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:51.322 |       Read project file 119def8b-e369-48ac-8f8d-35eff8ca0322 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/9f88f8a8-2d73-4038-89dc-b7b8c5070f35/AgentSchemaViewer.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:51.322 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:51.322 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ReviewLoopStepConfig.tsx
workflow-engine  | 2026-03-14 12:19:51.353 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:51.353 |       Read project file 28bafa1b-f96c-4668-b2fe-6962e345971d (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/91fb53d1-ebd1-4b8b-93fc-e4225e27c8b9/ReviewLoopStepConfig.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:51.353 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:51.353 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ReviewLoopNode.tsx
workflow-engine  | 2026-03-14 12:19:51.435 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:51.435 |       Read project file f5ba5079-1131-46c3-9fa1-3076eb7e4652 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/13cc9907-66fd-4388-95d8-174b64da2428/ReviewLoopNode.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:51.437 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:51.437 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ForEachStepConfig.tsx
workflow-engine  | 2026-03-14 12:19:51.505 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:51.507 |       Read project file 8d8d256b-a29c-43f3-aa61-8103f260ee42 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/a1e3907a-4a0a-49a7-b93b-d3a58aa93ee6/ForEachStepConfig.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:51.509 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:51.509 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ForEachNode.tsx
workflow-engine  | 2026-03-14 12:19:51.598 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:51.598 |       Read project file c144bcb7-e145-4dcb-adab-2eb6a7885141 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/a11431b7-f300-4fea-b1fe-e4ebf85846b1/ForEachNode.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:51.601 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:51.601 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ParallelNode.tsx
workflow-engine  | 2026-03-14 12:19:51.743 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:51.743 |       Read project file d629124b-5c73-4d41-856e-af739c6345ea (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/0b7c7a88-e791-4dd2-9409-8f945e72df7f/ParallelNode.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:51.762 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:51.762 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ConditionalNode.tsx
workflow-engine  | 2026-03-14 12:19:51.835 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:51.836 |       Read project file d1e524c5-ae23-4c15-b4f0-9e19e65c56bf (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/0a021852-c231-4212-9aa0-5880337bb4a5/ConditionalNode.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:51.838 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:51.838 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ConditionalStepConfig.tsx
workflow-engine  | 2026-03-14 12:19:51.917 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:51.923 |       Read project file f4b9008e-c744-458b-9e07-e3443ecafdbe (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/e06e2a0b-9906-40e9-8676-2a9e74f34841/ConditionalStepConfig.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:51.926 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:51.926 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ConfirmModal.tsx
workflow-engine  | 2026-03-14 12:19:52.001 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:52.001 |       Read project file ff38de1c-51f4-48aa-9e68-518322e26d53 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/b6be1637-7c7a-4812-93e6-4316fa4e806b/ConfirmModal.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:52.001 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:52.001 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ErrorAlert.tsx
workflow-engine  | 2026-03-14 12:19:52.104 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:52.105 |       Read project file 6239418c-dbbd-4947-b36f-1852fe02047a (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/a891946f-0a82-4174-aedc-8319c34e2dc9/ErrorAlert.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:52.105 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:52.105 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ExecuteWithInputModal.tsx
workflow-engine  | 2026-03-14 12:19:52.280 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:52.281 |       Read project file a0a04a09-f53e-4288-a93e-a82ed5478f8c (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/e26b3fd0-dfa1-4f03-b7e3-ffa485f32a7b/ExecuteWithInputModal.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:52.281 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:52.281 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference LoginForm.tsx
workflow-engine  | 2026-03-14 12:19:52.645 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:52.647 |       Read project file b610f7f0-1beb-47fd-9c44-19e686d934ea (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/91694f1a-c8c0-4618-8433-4efa1cd2038c/LoginForm.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:52.649 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:52.649 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference NavLinks.tsx
workflow-engine  | 2026-03-14 12:19:52.810 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:52.810 |       Read project file 259ee957-6186-4a4c-809b-5b29e4b2c849 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/9d183570-7a75-4fcc-b203-9974d6396233/NavLinks.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:52.818 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:52.818 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference PageHeader.tsx
workflow-engine  | 2026-03-14 12:19:52.946 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:52.963 |       Read project file 9c6a2238-e8e3-4e74-bd2b-350cb9d3109e (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/4b7749fa-b54c-4c8d-ba84-a1f037e7cb9d/PageHeader.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:52.963 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:52.963 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ProjectCard.tsx
workflow-engine  | 2026-03-14 12:19:54.869 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:54.870 |       Read project file 47097ee5-7df6-4018-bc49-7e8c30dd1eda (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/0ed481fb-74b4-44fc-a56f-068931b61e5c/ProjectCard.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:54.870 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:54.870 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ProjectForm.tsx
workflow-engine  | 2026-03-14 12:19:55.210 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:55.210 |       Read project file 1bb492a6-c317-4560-91ac-c84f468a9d40 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/a6347e2b-e2e2-4594-bf08-9abfa016e7c9/ProjectForm.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:55.210 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:55.211 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ThemeToggle.tsx
workflow-engine  | 2026-03-14 12:19:55.299 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:55.299 |       Read project file 3666305d-1099-4c7c-a176-0e0778658f7f (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/386c986e-dcdd-4acf-badf-a5ffa4220aa6/ThemeToggle.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:55.299 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:55.299 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference AppShell.tsx
workflow-engine  | 2026-03-14 12:19:55.364 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:55.364 |       Read project file 9a386623-a3ef-449f-a334-3daed8c6f1c0 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/5178350b-ab94-4a29-bf7a-87f965bd319b/AppShell.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:55.364 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:55.364 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference layout.tsx
workflow-engine  | 2026-03-14 12:19:55.490 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:55.491 |       Read project file 6430b51b-8045-45a3-92c1-8afadc07a2b2 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/ae1d5a06-33a1-4e72-938e-29c6082748de/layout.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:55.501 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:55.501 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference UserTable.tsx
workflow-engine  | 2026-03-14 12:19:55.566 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:55.566 |       Read project file af48bd1e-dcad-4095-809b-6307ad6621f5 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/e433d994-66a1-4bcd-92a0-e5edb844d2b2/UserTable.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:55.568 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:55.568 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference TempPasswordModal.tsx
workflow-engine  | 2026-03-14 12:19:55.663 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:55.668 |       Read project file 08ca7950-5411-4a1f-a38a-4b5de41cd8db (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/3c6f9775-dfc1-4acc-ae2a-3f7bdd5008e5/TempPasswordModal.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:55.671 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:55.671 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference EmptyState.tsx
workflow-engine  | 2026-03-14 12:19:55.816 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:55.832 |       Read project file c5c4633a-d7eb-4c50-a439-e846d8d4e469 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/10e8a8d7-a04a-4d6f-9435-a273913bed98/EmptyState.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:55.832 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:55.832 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference WorkflowStepList.tsx
workflow-engine  | 2026-03-14 12:19:56.028 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:56.028 |       Read project file 4daeac02-f4ca-48b6-9c8b-0ff4a4b1c32e (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/25e86607-d39b-4886-9c4c-778830af8259/WorkflowStepList.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:56.028 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:56.028 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference FlowchartBuilder.tsx
workflow-engine  | 2026-03-14 12:19:56.148 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:56.148 |       Read project file 1918eb49-5195-4e14-9fea-527b4a10ffce (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/83aaa908-5aac-4393-99db-0720135f6536/FlowchartBuilder.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:19:56.174 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:56.175 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference page.tsx
workflow-engine  | 2026-03-14 12:19:56.289 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:19:56.290 |       Read project file 920500df-f8bb-47fd-8b4c-8d5a794eda02 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/ac04c99c-954b-423c-a92e-f43e32e4be22/page.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:20:36.044 | info: ReelForge.WorkflowEngine.Execution.StepExecutors.ParallelStepExecutor[0]
workflow-engine  | 2026-03-14 12:20:36.044 |       Parallel step 1: agent ComponentInventoryAnalyzer responded with preview: {   "TotalComponents": 44,   "CommonPatterns": [     "Next.js use client directive for client components",     "React functional components with TypeScript typing",     "Mantine UI library for consistent design and layout",     "Controlled form compo
workflow-engine  | 2026-03-14 12:20:36.076 | info: ReelForge.WorkflowEngine.Execution.WorkflowExecutorService[0]
workflow-engine  | 2026-03-14 12:20:36.077 |       Step 1 (Parallel) completed for execution 5227a5ab-ab88-4405-8310-819fe90a494b with status Completed; duration 85797ms; tokens 521271; nextStepIndex 1
workflow-engine  | 2026-03-14 12:20:36.882 | info: ReelForge.WorkflowEngine.Execution.WorkflowEventPublisher[0]
workflow-engine  | 2026-03-14 12:20:36.882 |       Publishing step completed event: ExecutionId=5227a5ab-ab88-4405-8310-819fe90a494b, StepId=d0e0486d-3899-4cb4-9bef-4aa3dd2c77f6, Status=Completed
workflow-engine  | 2026-03-14 12:20:37.245 | info: ReelForge.WorkflowEngine.Execution.WorkflowExecutorService[0]
workflow-engine  | 2026-03-14 12:20:37.245 |       Executing step 2 (Parallel) for execution 5227a5ab-ab88-4405-8310-819fe90a494b, attempt 1/3
workflow-engine  | 2026-03-14 12:20:37.299 | info: ReelForge.WorkflowEngine.Execution.StepExecutors.ParallelStepExecutor[0]
workflow-engine  | 2026-03-14 12:20:37.299 |       Parallel step 2: running 2 agents in parallel: Scriptwriter, AnimationStrategy
workflow-engine  | 2026-03-14 12:20:39.555 | info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
workflow-engine  | 2026-03-14 12:20:39.556 |       Start processing HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files?*
workflow-engine  | 2026-03-14 12:20:39.559 | info: System.Net.Http.HttpClient.Default.ClientHandler[100]
workflow-engine  | 2026-03-14 12:20:39.559 |       Sending HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files?*
workflow-engine  | 2026-03-14 12:20:39.614 | info: System.Net.Http.HttpClient.Default.ClientHandler[101]
workflow-engine  | 2026-03-14 12:20:39.615 |       Received HTTP response headers after 53.7656ms - 404
workflow-engine  | 2026-03-14 12:20:39.616 | info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
workflow-engine  | 2026-03-14 12:20:39.616 |       End processing HTTP request after 72.3222ms - 404
workflow-engine  | 2026-03-14 12:20:39.628 | info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
workflow-engine  | 2026-03-14 12:20:39.628 |       Start processing HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files?*
workflow-engine  | 2026-03-14 12:20:39.643 | info: System.Net.Http.HttpClient.Default.ClientHandler[100]
workflow-engine  | 2026-03-14 12:20:39.644 |       Sending HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files?*
workflow-engine  | 2026-03-14 12:20:39.652 | info: System.Net.Http.HttpClient.Default.ClientHandler[101]
workflow-engine  | 2026-03-14 12:20:39.652 |       Received HTTP response headers after 9.0822ms - 404
workflow-engine  | 2026-03-14 12:20:39.652 | info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
workflow-engine  | 2026-03-14 12:20:39.652 |       End processing HTTP request after 26.9754ms - 404
workflow-engine  | 2026-03-14 12:20:39.652 | info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
workflow-engine  | 2026-03-14 12:20:39.652 |       Start processing HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files?*
workflow-engine  | 2026-03-14 12:20:39.652 | info: System.Net.Http.HttpClient.Default.ClientHandler[100]
workflow-engine  | 2026-03-14 12:20:39.653 |       Sending HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files?*
workflow-engine  | 2026-03-14 12:20:39.662 | info: System.Net.Http.HttpClient.Default.ClientHandler[101]
workflow-engine  | 2026-03-14 12:20:39.663 |       Received HTTP response headers after 8.2316ms - 404
workflow-engine  | 2026-03-14 12:20:39.663 | info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
workflow-engine  | 2026-03-14 12:20:39.663 |       End processing HTTP request after 9.985ms - 404
workflow-engine  | 2026-03-14 12:20:39.702 | info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
workflow-engine  | 2026-03-14 12:20:39.702 |       Start processing HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files?*
workflow-engine  | 2026-03-14 12:20:39.702 | info: System.Net.Http.HttpClient.Default.ClientHandler[100]
workflow-engine  | 2026-03-14 12:20:39.702 |       Sending HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files?*
workflow-engine  | 2026-03-14 12:20:39.702 | info: System.Net.Http.HttpClient.Default.ClientHandler[101]
workflow-engine  | 2026-03-14 12:20:39.702 |       Received HTTP response headers after 24.7651ms - 404
workflow-engine  | 2026-03-14 12:20:39.702 | info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
workflow-engine  | 2026-03-14 12:20:39.702 |       End processing HTTP request after 26.0785ms - 404
workflow-engine  | 2026-03-14 12:20:39.731 | info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
workflow-engine  | 2026-03-14 12:20:39.731 |       Start processing HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files?*
workflow-engine  | 2026-03-14 12:20:39.731 | info: System.Net.Http.HttpClient.Default.ClientHandler[100]
workflow-engine  | 2026-03-14 12:20:39.731 |       Sending HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files?*
workflow-engine  | 2026-03-14 12:20:39.731 | info: System.Net.Http.HttpClient.Default.ClientHandler[101]
workflow-engine  | 2026-03-14 12:20:39.731 |       Received HTTP response headers after 19.9554ms - 404
workflow-engine  | 2026-03-14 12:20:39.731 | info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
workflow-engine  | 2026-03-14 12:20:39.731 |       End processing HTTP request after 20.7091ms - 404
workflow-engine  | 2026-03-14 12:20:40.019 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:20:40.019 |       Listing project files for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:20:40.070 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:20:40.070 |       Found 108 files for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:20:41.198 | info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
workflow-engine  | 2026-03-14 12:20:41.199 |       Start processing HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files?*
workflow-engine  | 2026-03-14 12:20:41.199 | info: System.Net.Http.HttpClient.Default.ClientHandler[100]
workflow-engine  | 2026-03-14 12:20:41.199 |       Sending HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files?*
workflow-engine  | 2026-03-14 12:20:41.199 | info: System.Net.Http.HttpClient.Default.ClientHandler[101]
workflow-engine  | 2026-03-14 12:20:41.199 |       Received HTTP response headers after 8.2766ms - 404
workflow-engine  | 2026-03-14 12:20:41.199 | info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
workflow-engine  | 2026-03-14 12:20:41.199 |       End processing HTTP request after 9.9628ms - 404
workflow-engine  | 2026-03-14 12:20:42.617 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:20:42.617 |       Listing project files for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:20:42.714 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:20:42.714 |       Found 108 files for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:20:48.321 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:20:48.322 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference render-manifest-final.json
workflow-engine  | 2026-03-14 12:20:48.546 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:20:48.546 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference WorkflowStepList.tsx
workflow-engine  | 2026-03-14 12:20:48.669 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:20:48.669 |       Read project file 4daeac02-f4ca-48b6-9c8b-0ff4a4b1c32e (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/25e86607-d39b-4886-9c4c-778830af8259/WorkflowStepList.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:20:52.337 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:20:52.338 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference StepCard.tsx
workflow-engine  | 2026-03-14 12:20:52.413 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:20:52.413 |       Read project file 64dd2d28-947f-4630-9fd1-d879cf2510f1 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/c72cb11e-f7eb-42a6-b61e-84c6b01a9515/StepCard.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:20:55.906 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:20:55.906 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference FlowchartBuilder.tsx
workflow-engine  | 2026-03-14 12:20:55.992 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:20:55.996 |       Read project file 1918eb49-5195-4e14-9fea-527b4a10ffce (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/83aaa908-5aac-4393-99db-0720135f6536/FlowchartBuilder.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:20:56.408 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:20:56.411 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference render-manifest.json
workflow-engine  | 2026-03-14 12:20:59.327 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:20:59.327 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ExecutionProgress.tsx
workflow-engine  | 2026-03-14 12:20:59.332 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:20:59.332 |       Read project file 10129a03-185e-45f8-b540-1eb953706eeb (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/b6b1a654-cc08-4df4-a973-b9970004296b/ExecutionProgress.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:21:00.177 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:21:00.177 |       Reading project file for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71 using reference ExecutionHistory.tsx
workflow-engine  | 2026-03-14 12:21:00.184 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:21:00.184 |       Read project file 6e580d15-0290-40ff-94ed-34c5d4a7c8a3 (projects/460cacfd-d1bc-4e46-a26a-5e8c7114dd71/userFiles/3a55e05a-e7c4-43fe-b6a7-181bdca2756c/ExecutionHistory.tsx) for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
inference        | 2026-03-14 12:21:05.431 | info: MassTransit[0]
inference        | 2026-03-14 12:21:05.431 |       Usage Telemetry: {"id":"01000000-06bd-0e5c-84ed-08de81bb077e","created":"2026-03-14T11:15:41.7789275\u002B00:00","host":{"framework_version":"9.0.14","mass_transit_version":"8.5.8.0","operating_system_version":"Unix 5.15.167.4","time_zone_info":"(UTC) Coordinated Universal Time","commit_hash":"c1e5caa9a49ba4e56dbad6ed1b9c8f7843a9dba5","container":"Docker"},"bus":[{"created":"2026-03-14T11:15:41.7807854\u002B00:00","configured":"2026-03-14T11:15:41.8235503\u002B00:00","started":"2026-03-14T11:15:42.1198744\u002B00:00","name":"IBus","endpoints":[{"name":"_bus_","type":"RabbitMQ","prefetch_count":24}]}]}
workflow-engine  | 2026-03-14 12:21:08.659 | info: ReelForge.WorkflowEngine.Execution.StepExecutors.ParallelStepExecutor[0]
workflow-engine  | 2026-03-14 12:21:08.659 |       Parallel step 2: agent AnimationStrategy responded with preview: {   "scenes": [     {       "id": "scene_01",       "componentName": "WorkflowStepList",       "startFrame": 0,       "durationInFrames": 180,       "transitionType": "slide",       "transitionDurationInFrames": 20,       "animations": [         {   
workflow-engine  | 2026-03-14 12:21:11.387 | info: MassTransit[0]
workflow-engine  | 2026-03-14 12:21:11.387 |       Usage Telemetry: {"id":"01000000-bb76-731b-28ce-08de81bb0add","created":"2026-03-14T11:15:47.4324409\u002B00:00","host":{"framework_version":"9.0.14","mass_transit_version":"8.5.8.0","operating_system_version":"Unix 5.15.167.4","time_zone_info":"(UTC) Coordinated Universal Time","commit_hash":"c1e5caa9a49ba4e56dbad6ed1b9c8f7843a9dba5","container":"Docker"},"bus":[{"created":"2026-03-14T11:15:47.4374754\u002B00:00","configured":"2026-03-14T11:15:47.6588798\u002B00:00","started":"2026-03-14T11:15:48.0865327\u002B00:00","name":"IBus","endpoints":[{"name":"workflow-execution","type":"RabbitMQ","prefetch_count":4,"concurrent_message_limit":4,"consumer_count":1,"message_count":1},{"name":"workflow-stop-requests","type":"RabbitMQ","prefetch_count":24,"consumer_count":1,"message_count":1},{"name":"_bus_","type":"RabbitMQ","prefetch_count":24}]}]}
workflow-engine  | 2026-03-14 12:21:26.447 | info: ReelForge.WorkflowEngine.Execution.StepExecutors.ParallelStepExecutor[0]
workflow-engine  | 2026-03-14 12:21:26.447 |       Parallel step 2: agent Scriptwriter responded with preview: {   "Title": "ReelForge — AI-Powered Promo Videos in Minutes",   "DurationInSeconds": 60,   "Narrative": "A fast, visual story that shows how ReelForge replaces slow, expensive promo production with a visual workflow builder, intelligent agents, live
workflow-engine  | 2026-03-14 12:21:26.448 | info: ReelForge.WorkflowEngine.Execution.WorkflowExecutorService[0]
workflow-engine  | 2026-03-14 12:21:26.448 |       Step 2 (Parallel) completed for execution 5227a5ab-ab88-4405-8310-819fe90a494b with status Completed; duration 72362ms; tokens 681634; nextStepIndex 2
workflow-engine  | 2026-03-14 12:21:26.463 | info: ReelForge.WorkflowEngine.Execution.WorkflowEventPublisher[0]
workflow-engine  | 2026-03-14 12:21:26.463 |       Publishing step completed event: ExecutionId=5227a5ab-ab88-4405-8310-819fe90a494b, StepId=22b3901f-0d64-408e-b7d0-8d1a2074b0f5, Status=Completed
workflow-engine  | 2026-03-14 12:21:26.469 | info: ReelForge.WorkflowEngine.Execution.WorkflowExecutorService[0]
workflow-engine  | 2026-03-14 12:21:26.469 |       Executing step 3 (Agent) for execution 5227a5ab-ab88-4405-8310-819fe90a494b, attempt 1/3
workflow-engine  | 2026-03-14 12:21:26.471 | info: ReelForge.WorkflowEngine.Execution.StepExecutors.AgentStepExecutor[0]
workflow-engine  | 2026-03-14 12:21:26.471 |       Executing agent step 3: Author
workflow-engine  | 2026-03-14 12:21:28.776 | info: ReelForge.WorkflowEngine.Agents.Tools.ReactRemotionSandboxTools[0]
workflow-engine  | 2026-03-14 12:21:28.776 |       Ensuring sandbox exists for execution 5227a5ab-ab88-4405-8310-819fe90a494b
workflow-engine  | 2026-03-14 12:21:28.778 | info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
workflow-engine  | 2026-03-14 12:21:28.778 |       Start processing HTTP request POST http://sandbox-executor:8080/api/v1/sandboxes
workflow-engine  | 2026-03-14 12:21:28.778 | info: System.Net.Http.HttpClient.Default.ClientHandler[100]
workflow-engine  | 2026-03-14 12:21:28.778 |       Sending HTTP request POST http://sandbox-executor:8080/api/v1/sandboxes
sandbox-executor | 2026-03-14 12:21:28.790 | 2026/03/14 11:21:28 API create sandbox request for workflow 5227a5ab-ab88-4405-8310-819fe90a494b
sandbox-executor | 2026-03-14 12:21:28.794 | 2026/03/14 11:21:28 [docker] running: docker run -d --name rf-sbx-fbb66437-961f-49f0-b695-c6a496daa3ec --network sandbox-net --read-only --tmpfs /tmp:rw,nosuid,nodev,size=256m --tmpfs /home/node/.npm:rw,nosuid,nodev,size=512m --memory 2g --cpus 2 --pids-limit 256 --security-opt no-new-privileges --cap-drop ALL --user node -v /var/lib/reelforge/sandboxes/fbb66437-961f-49f0-b695-c6a496daa3ec:/workspace -w /workspace --entrypoint sh reelforge-sandbox-executor:local -c if [ ! -f /workspace/package.json ]; then \
sandbox-executor | 2026-03-14 12:21:28.794 | 		cp -r /opt/remotion-template/. /workspace/; \
sandbox-executor | 2026-03-14 12:21:28.794 | 		# make sure the path for the headless-shell binary exists. if Remotion has
sandbox-executor | 2026-03-14 12:21:28.794 | 		# already downloaded the real chrome-headless-shell binary during npm
sandbox-executor | 2026-03-14 12:21:28.794 | 		# install, leave it alone; otherwise fall back to symlinking the system
sandbox-executor | 2026-03-14 12:21:28.794 | 		# Chromium binary so that any stray spawn attempts succeed.
sandbox-executor | 2026-03-14 12:21:28.794 | 		mkdir -p /workspace/node_modules/.remotion/chrome-headless-shell/linux64/chrome-headless-shell-linux64/; \
sandbox-executor | 2026-03-14 12:21:28.794 | 		if [ ! -x /workspace/node_modules/.remotion/chrome-headless-shell/linux64/chrome-headless-shell-linux64/chrome-headless-shell ]; then \
sandbox-executor | 2026-03-14 12:21:28.794 | 			ln -sf /usr/bin/chromium \
sandbox-executor | 2026-03-14 12:21:28.794 | 				/workspace/node_modules/.remotion/chrome-headless-shell/linux64/chrome-headless-shell-linux64/chrome-headless-shell || true; \
sandbox-executor | 2026-03-14 12:21:28.794 | 		fi; \
sandbox-executor | 2026-03-14 12:21:28.794 | 	fi; sleep infinity
sandbox-executor | 2026-03-14 12:21:29.485 | 2026/03/14 11:21:29 [docker] output: 0e73bcbd5312825a72ff48946f7cad784737ab86d8c1de7775e23fe8c6508524
sandbox-executor | 2026-03-14 12:21:29.485 | 2026/03/14 11:21:29 sandbox container rf-sbx-fbb66437-961f-49f0-b695-c6a496daa3ec created for workflow 5227a5ab-ab88-4405-8310-819fe90a494b
sandbox-executor | 2026-03-14 12:21:29.485 | 2026/03/14 11:21:29 waiting for template init in sandbox rf-sbx-fbb66437-961f-49f0-b695-c6a496daa3ec (workflow 5227a5ab-ab88-4405-8310-819fe90a494b)
sandbox-executor | 2026-03-14 12:21:58.503 | 2026/03/14 11:21:58 sandbox rf-sbx-fbb66437-961f-49f0-b695-c6a496daa3ec ready (workflow 5227a5ab-ab88-4405-8310-819fe90a494b)
workflow-engine  | 2026-03-14 12:21:58.507 | info: System.Net.Http.HttpClient.Default.ClientHandler[101]
workflow-engine  | 2026-03-14 12:21:58.507 |       Received HTTP response headers after 27001.3212ms - 201
workflow-engine  | 2026-03-14 12:21:58.507 | info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
workflow-engine  | 2026-03-14 12:21:58.507 |       End processing HTTP request after 27001.4872ms - 201
workflow-engine  | 2026-03-14 12:21:59.266 | info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
workflow-engine  | 2026-03-14 12:21:59.267 |       Start processing HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files?*
workflow-engine  | 2026-03-14 12:21:59.267 | info: System.Net.Http.HttpClient.Default.ClientHandler[100]
workflow-engine  | 2026-03-14 12:21:59.267 |       Sending HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files?*
workflow-engine  | 2026-03-14 12:21:59.270 | info: System.Net.Http.HttpClient.Default.ClientHandler[101]
workflow-engine  | 2026-03-14 12:21:59.270 |       Received HTTP response headers after 3.6624ms - 200
workflow-engine  | 2026-03-14 12:21:59.270 | info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
workflow-engine  | 2026-03-14 12:21:59.270 |       End processing HTTP request after 3.8374ms - 200
workflow-engine  | 2026-03-14 12:22:00.071 | info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
workflow-engine  | 2026-03-14 12:22:00.071 |       Start processing HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files/content?*
workflow-engine  | 2026-03-14 12:22:00.071 | info: System.Net.Http.HttpClient.Default.ClientHandler[100]
workflow-engine  | 2026-03-14 12:22:00.071 |       Sending HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files/content?*
workflow-engine  | 2026-03-14 12:22:00.076 | info: System.Net.Http.HttpClient.Default.ClientHandler[101]
workflow-engine  | 2026-03-14 12:22:00.076 |       Received HTTP response headers after 4.5147ms - 200
workflow-engine  | 2026-03-14 12:22:00.076 | info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
workflow-engine  | 2026-03-14 12:22:00.076 |       End processing HTTP request after 4.9214ms - 200
workflow-engine  | 2026-03-14 12:22:00.764 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:22:00.764 |       Listing project files for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:22:00.776 | info: ReelForge.WorkflowEngine.Services.Storage.ProjectFileWorkspace[0]
workflow-engine  | 2026-03-14 12:22:00.776 |       Found 108 files for project 460cacfd-d1bc-4e46-a26a-5e8c7114dd71
workflow-engine  | 2026-03-14 12:22:03.742 | info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
workflow-engine  | 2026-03-14 12:22:03.742 |       Start processing HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files/content?*
workflow-engine  | 2026-03-14 12:22:03.742 | info: System.Net.Http.HttpClient.Default.ClientHandler[100]
workflow-engine  | 2026-03-14 12:22:03.742 |       Sending HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files/content?*
workflow-engine  | 2026-03-14 12:22:03.745 | info: System.Net.Http.HttpClient.Default.ClientHandler[101]
workflow-engine  | 2026-03-14 12:22:03.745 |       Received HTTP response headers after 3.6675ms - 200
workflow-engine  | 2026-03-14 12:22:03.745 | info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
workflow-engine  | 2026-03-14 12:22:03.745 |       End processing HTTP request after 3.8196ms - 200
workflow-engine  | 2026-03-14 12:22:16.900 | info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
workflow-engine  | 2026-03-14 12:22:16.900 |       Start processing HTTP request PUT http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files/content?*
workflow-engine  | 2026-03-14 12:22:16.900 | info: System.Net.Http.HttpClient.Default.ClientHandler[100]
workflow-engine  | 2026-03-14 12:22:16.900 |       Sending HTTP request PUT http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files/content?*
workflow-engine  | 2026-03-14 12:22:16.902 | info: System.Net.Http.HttpClient.Default.ClientHandler[101]
workflow-engine  | 2026-03-14 12:22:16.902 |       Received HTTP response headers after 2.9522ms - 200
workflow-engine  | 2026-03-14 12:22:16.902 | info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
workflow-engine  | 2026-03-14 12:22:16.902 |       End processing HTTP request after 3.0685ms - 200
workflow-engine  | 2026-03-14 12:22:17.922 | info: ReelForge.WorkflowEngine.Agents.Tools.ReactRemotionSandboxTools[0]
workflow-engine  | 2026-03-14 12:22:17.922 |       Running typecheck/lint in sandbox for execution 5227a5ab-ab88-4405-8310-819fe90a494b
workflow-engine  | 2026-03-14 12:22:17.925 | info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
workflow-engine  | 2026-03-14 12:22:17.925 |       Start processing HTTP request POST http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/exec
workflow-engine  | 2026-03-14 12:22:17.925 | info: System.Net.Http.HttpClient.Default.ClientHandler[100]
workflow-engine  | 2026-03-14 12:22:17.925 |       Sending HTTP request POST http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/exec
sandbox-executor | 2026-03-14 12:22:17.926 | 2026/03/14 11:22:17 [docker] running: docker exec rf-sbx-fbb66437-961f-49f0-b695-c6a496daa3ec sh -c mkdir -p /workspace/node_modules/.remotion/chrome-headless-shell/linux64/chrome-headless-shell-linux64/ && if [ ! -x /workspace/node_modules/.remotion/chrome-headless-shell/linux64/chrome-headless-shell-linux64/chrome-headless-shell ]; then ln -sf /usr/bin/chromium /workspace/node_modules/.remotion/chrome-headless-shell/linux64/chrome-headless-shell-linux64/chrome-headless-shell ; fi || true
sandbox-executor | 2026-03-14 12:22:18.019 | 2026/03/14 11:22:18 exec request for workflow 5227a5ab-ab88-4405-8310-819fe90a494b: npm [run typecheck]
sandbox-executor | 2026-03-14 12:22:18.019 | 2026/03/14 11:22:18 [docker] running: docker exec -w /workspace rf-sbx-fbb66437-961f-49f0-b695-c6a496daa3ec npm run typecheck
sandbox-executor | 2026-03-14 12:22:24.785 | 2026/03/14 11:22:24 [docker] output: 
sandbox-executor | 2026-03-14 12:22:24.785 | > typecheck
sandbox-executor | 2026-03-14 12:22:24.785 | > tsc --noEmit
sandbox-executor | 2026-03-14 12:22:24.785 | 
sandbox-executor | 2026-03-14 12:22:24.785 | src/index.ts(4,22): error TS5097: An import path can only end with a '.tsx' extension when 'allowImportingTsExtensions' is enabled.
sandbox-executor | 2026-03-14 12:22:24.785 | src/root.tsx(5,32): error TS2307: Cannot find module './WorkflowStepList.tsx' or its corresponding type declarations.
sandbox-executor | 2026-03-14 12:22:24.785 | src/root.tsx(6,24): error TS2307: Cannot find module './StepCard.tsx' or its corresponding type declarations.
sandbox-executor | 2026-03-14 12:22:24.785 | src/root.tsx(7,39): error TS2307: Cannot find module './FlowchartBuilder.tsx' or its corresponding type declarations.
sandbox-executor | 2026-03-14 12:22:24.785 | src/root.tsx(8,33): error TS2307: Cannot find module './ExecutionProgress.tsx' or its corresponding type declarations.
sandbox-executor | 2026-03-14 12:22:24.785 | src/root.tsx(9,32): error TS2307: Cannot find module './ExecutionHistory.tsx' or its corresponding type declarations.
sandbox-executor | 2026-03-14 12:22:24.785 | src/root.tsx(110,64): error TS2339: Property 'fps' does not exist on type '{ id: string; componentName: string; startFrame: number; durationInFrames: number; props: {}; script: { voiceover: string; captions: string[]; }; }'.
sandbox-executor | 2026-03-14 12:22:24.785 | src/root.tsx(120,11): error TS2322: Type '{ key: string; id: string; component: any; durationInFrames: number; fps: any; width: number; height: number; defaultProps: {}; startFrame: number; }' is not assignable to type 'IntrinsicAttributes & CompositionProps<AnyZodObject, {}>'.
sandbox-executor | 2026-03-14 12:22:24.785 |   Property 'startFrame' does not exist on type 'IntrinsicAttributes & CompositionProps<AnyZodObject, {}>'.
sandbox-executor | 2026-03-14 12:22:24.785 | npm notice
sandbox-executor | 2026-03-14 12:22:24.785 | npm notice New major version of npm available! 10.9.4 -> 11.11.1
sandbox-executor | 2026-03-14 12:22:24.785 | npm notice Changelog: https://github.com/npm/cli/releases/tag/v11.11.1
sandbox-executor | 2026-03-14 12:22:24.785 | npm notice To update run: npm install -g npm@11.11.1
sandbox-executor | 2026-03-14 12:22:24.785 | npm notice
sandbox-executor | 2026-03-14 12:22:24.785 | 2026/03/14 11:22:24 [docker] error: exit status 2
sandbox-executor | 2026-03-14 12:22:24.785 | 2026/03/14 11:22:24 exec failed for workflow 5227a5ab-ab88-4405-8310-819fe90a494b: exit status 2; output: 
sandbox-executor | 2026-03-14 12:22:24.785 | > typecheck
sandbox-executor | 2026-03-14 12:22:24.785 | > tsc --noEmit
sandbox-executor | 2026-03-14 12:22:24.785 | 
sandbox-executor | 2026-03-14 12:22:24.785 | src/index.ts(4,22): error TS5097: An import path can only end with a '.tsx' extension when 'allowImportingTsExtensions' is enabled.
sandbox-executor | 2026-03-14 12:22:24.785 | src/root.tsx(5,32): error TS2307: Cannot find module './WorkflowStepList.tsx' or its corresponding type declarations.
sandbox-executor | 2026-03-14 12:22:24.785 | src/root.tsx(6,24): error TS2307: Cannot find module './StepCard.tsx' or its corresponding type declarations.
sandbox-executor | 2026-03-14 12:22:24.785 | src/root.tsx(7,39): error TS2307: Cannot find module './FlowchartBuilder.tsx' or its corresponding type declarations.
sandbox-executor | 2026-03-14 12:22:24.785 | src/root.tsx(8,33): error TS2307: Cannot find module './ExecutionProgress.tsx' or its corresponding type declarations.
sandbox-executor | 2026-03-14 12:22:24.785 | src/root.tsx(9,32): error TS2307: Cannot find module './ExecutionHistory.tsx' or its corresponding type declarations.
sandbox-executor | 2026-03-14 12:22:24.785 | src/root.tsx(110,64): error TS2339: Property 'fps' does not exist on type '{ id: string; componentName: string; startFrame: number; durationInFrames: number; props: {}; script: { voiceover: string; captions: string[]; }; }'.
sandbox-executor | 2026-03-14 12:22:24.785 | src/root.tsx(120,11): error TS2322: Type '{ key: string; id: string; component: any; durationInFrames: number; fps: any; width: number; height: number; defaultProps: {}; startFrame: number; }' is not assignable to type 'IntrinsicAttributes & CompositionProps<AnyZodObject, {}>'.
sandbox-executor | 2026-03-14 12:22:24.785 |   Property 'startFrame' does not exist on type 'IntrinsicAttributes & CompositionProps<AnyZodObject, {}>'.
sandbox-executor | 2026-03-14 12:22:24.785 | npm notice
sandbox-executor | 2026-03-14 12:22:24.785 | npm notice New major version of npm available! 10.9.4 -> 11.11.1
sandbox-executor | 2026-03-14 12:22:24.785 | npm notice Changelog: https://github.com/npm/cli/releases/tag/v11.11.1
sandbox-executor | 2026-03-14 12:22:24.785 | npm notice To update run: npm install -g npm@11.11.1
sandbox-executor | 2026-03-14 12:22:24.785 | npm notice
workflow-engine  | 2026-03-14 12:22:24.788 | info: System.Net.Http.HttpClient.Default.ClientHandler[101]
workflow-engine  | 2026-03-14 12:22:24.788 |       Received HTTP response headers after 4135.2901ms - 500
workflow-engine  | 2026-03-14 12:22:24.788 | info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
workflow-engine  | 2026-03-14 12:22:24.788 |       End processing HTTP request after 4135.4335ms - 500
workflow-engine  | 2026-03-14 12:22:24.796 | info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
workflow-engine  | 2026-03-14 12:22:24.796 |       Start processing HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files/content?*
workflow-engine  | 2026-03-14 12:22:24.796 | info: System.Net.Http.HttpClient.Default.ClientHandler[100]
workflow-engine  | 2026-03-14 12:22:24.796 |       Sending HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files/content?*
workflow-engine  | 2026-03-14 12:22:24.796 | info: System.Net.Http.HttpClient.Default.ClientHandler[101]
workflow-engine  | 2026-03-14 12:22:24.796 |       Received HTTP response headers after 0.6584ms - 200
workflow-engine  | 2026-03-14 12:22:24.796 | info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
workflow-engine  | 2026-03-14 12:22:24.796 |       End processing HTTP request after 0.8028ms - 200
workflow-engine  | 2026-03-14 12:22:26.004 | info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
workflow-engine  | 2026-03-14 12:22:26.004 |       Start processing HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files/content?*
workflow-engine  | 2026-03-14 12:22:26.004 | info: System.Net.Http.HttpClient.Default.ClientHandler[100]
workflow-engine  | 2026-03-14 12:22:26.004 |       Sending HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files/content?*
workflow-engine  | 2026-03-14 12:22:26.006 | info: System.Net.Http.HttpClient.Default.ClientHandler[101]
workflow-engine  | 2026-03-14 12:22:26.006 |       Received HTTP response headers after 1.1179ms - 200
workflow-engine  | 2026-03-14 12:22:26.006 | info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
workflow-engine  | 2026-03-14 12:22:26.006 |       End processing HTTP request after 1.2022ms - 200
workflow-engine  | 2026-03-14 12:22:34.274 | info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
workflow-engine  | 2026-03-14 12:22:34.274 |       Start processing HTTP request PUT http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files/content?*
workflow-engine  | 2026-03-14 12:22:34.274 | info: System.Net.Http.HttpClient.Default.ClientHandler[100]
workflow-engine  | 2026-03-14 12:22:34.274 |       Sending HTTP request PUT http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files/content?*
workflow-engine  | 2026-03-14 12:22:34.275 | info: System.Net.Http.HttpClient.Default.ClientHandler[101]
workflow-engine  | 2026-03-14 12:22:34.275 |       Received HTTP response headers after 1.048ms - 200
workflow-engine  | 2026-03-14 12:22:34.275 | info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
workflow-engine  | 2026-03-14 12:22:34.275 |       End processing HTTP request after 1.1905ms - 200
workflow-engine  | 2026-03-14 12:22:36.298 | info: ReelForge.WorkflowEngine.Agents.Tools.ReactRemotionSandboxTools[0]
workflow-engine  | 2026-03-14 12:22:36.299 |       Running typecheck/lint in sandbox for execution 5227a5ab-ab88-4405-8310-819fe90a494b
workflow-engine  | 2026-03-14 12:22:36.299 | info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
workflow-engine  | 2026-03-14 12:22:36.299 |       Start processing HTTP request POST http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/exec
workflow-engine  | 2026-03-14 12:22:36.299 | info: System.Net.Http.HttpClient.Default.ClientHandler[100]
workflow-engine  | 2026-03-14 12:22:36.299 |       Sending HTTP request POST http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/exec
sandbox-executor | 2026-03-14 12:22:36.299 | 2026/03/14 11:22:36 [docker] running: docker exec rf-sbx-fbb66437-961f-49f0-b695-c6a496daa3ec sh -c mkdir -p /workspace/node_modules/.remotion/chrome-headless-shell/linux64/chrome-headless-shell-linux64/ && if [ ! -x /workspace/node_modules/.remotion/chrome-headless-shell/linux64/chrome-headless-shell-linux64/chrome-headless-shell ]; then ln -sf /usr/bin/chromium /workspace/node_modules/.remotion/chrome-headless-shell/linux64/chrome-headless-shell-linux64/chrome-headless-shell ; fi || true
sandbox-executor | 2026-03-14 12:22:36.389 | 2026/03/14 11:22:36 exec request for workflow 5227a5ab-ab88-4405-8310-819fe90a494b: npm [run typecheck]
sandbox-executor | 2026-03-14 12:22:36.389 | 2026/03/14 11:22:36 [docker] running: docker exec -w /workspace rf-sbx-fbb66437-961f-49f0-b695-c6a496daa3ec npm run typecheck
sandbox-executor | 2026-03-14 12:22:38.397 | 2026/03/14 11:22:38 [docker] output: 
sandbox-executor | 2026-03-14 12:22:38.397 | > typecheck
sandbox-executor | 2026-03-14 12:22:38.397 | > tsc --noEmit
sandbox-executor | 2026-03-14 12:22:38.397 | 
sandbox-executor | 2026-03-14 12:22:38.397 | src/index.ts(4,22): error TS5097: An import path can only end with a '.tsx' extension when 'allowImportingTsExtensions' is enabled.
sandbox-executor | 2026-03-14 12:22:38.397 | src/root.tsx(4,34): error TS2834: Relative import paths need explicit file extensions in ECMAScript imports when '--moduleResolution' is 'node16' or 'nodenext'. Consider adding an extension to the import path.
sandbox-executor | 2026-03-14 12:22:38.397 | src/root.tsx(5,26): error TS2834: Relative import paths need explicit file extensions in ECMAScript imports when '--moduleResolution' is 'node16' or 'nodenext'. Consider adding an extension to the import path.
sandbox-executor | 2026-03-14 12:22:38.397 | src/root.tsx(6,41): error TS2834: Relative import paths need explicit file extensions in ECMAScript imports when '--moduleResolution' is 'node16' or 'nodenext'. Consider adding an extension to the import path.
sandbox-executor | 2026-03-14 12:22:38.397 | src/root.tsx(7,35): error TS2834: Relative import paths need explicit file extensions in ECMAScript imports when '--moduleResolution' is 'node16' or 'nodenext'. Consider adding an extension to the import path.
sandbox-executor | 2026-03-14 12:22:38.397 | src/root.tsx(8,34): error TS2834: Relative import paths need explicit file extensions in ECMAScript imports when '--moduleResolution' is 'node16' or 'nodenext'. Consider adding an extension to the import path.
sandbox-executor | 2026-03-14 12:22:38.397 | 2026/03/14 11:22:38 [docker] error: exit status 2
sandbox-executor | 2026-03-14 12:22:38.397 | 2026/03/14 11:22:38 exec failed for workflow 5227a5ab-ab88-4405-8310-819fe90a494b: exit status 2; output: 
sandbox-executor | 2026-03-14 12:22:38.397 | > typecheck
sandbox-executor | 2026-03-14 12:22:38.397 | > tsc --noEmit
sandbox-executor | 2026-03-14 12:22:38.397 | 
sandbox-executor | 2026-03-14 12:22:38.397 | src/index.ts(4,22): error TS5097: An import path can only end with a '.tsx' extension when 'allowImportingTsExtensions' is enabled.
sandbox-executor | 2026-03-14 12:22:38.397 | src/root.tsx(4,34): error TS2834: Relative import paths need explicit file extensions in ECMAScript imports when '--moduleResolution' is 'node16' or 'nodenext'. Consider adding an extension to the import path.
sandbox-executor | 2026-03-14 12:22:38.397 | src/root.tsx(5,26): error TS2834: Relative import paths need explicit file extensions in ECMAScript imports when '--moduleResolution' is 'node16' or 'nodenext'. Consider adding an extension to the import path.
sandbox-executor | 2026-03-14 12:22:38.397 | src/root.tsx(6,41): error TS2834: Relative import paths need explicit file extensions in ECMAScript imports when '--moduleResolution' is 'node16' or 'nodenext'. Consider adding an extension to the import path.
sandbox-executor | 2026-03-14 12:22:38.397 | src/root.tsx(7,35): error TS2834: Relative import paths need explicit file extensions in ECMAScript imports when '--moduleResolution' is 'node16' or 'nodenext'. Consider adding an extension to the import path.
sandbox-executor | 2026-03-14 12:22:38.397 | src/root.tsx(8,34): error TS2834: Relative import paths need explicit file extensions in ECMAScript imports when '--moduleResolution' is 'node16' or 'nodenext'. Consider adding an extension to the import path.
workflow-engine  | 2026-03-14 12:22:38.397 | info: System.Net.Http.HttpClient.Default.ClientHandler[101]
workflow-engine  | 2026-03-14 12:22:38.397 |       Received HTTP response headers after 2098.7631ms - 500
workflow-engine  | 2026-03-14 12:22:38.397 | info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
workflow-engine  | 2026-03-14 12:22:38.397 |       End processing HTTP request after 2098.8629ms - 500
workflow-engine  | 2026-03-14 12:22:38.398 | info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
workflow-engine  | 2026-03-14 12:22:38.398 |       Start processing HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files/content?*
workflow-engine  | 2026-03-14 12:22:38.398 | info: System.Net.Http.HttpClient.Default.ClientHandler[100]
workflow-engine  | 2026-03-14 12:22:38.398 |       Sending HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files/content?*
workflow-engine  | 2026-03-14 12:22:38.398 | info: System.Net.Http.HttpClient.Default.ClientHandler[101]
workflow-engine  | 2026-03-14 12:22:38.398 |       Received HTTP response headers after 0.6943ms - 200
workflow-engine  | 2026-03-14 12:22:38.398 | info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
workflow-engine  | 2026-03-14 12:22:38.398 |       End processing HTTP request after 0.7848ms - 200
workflow-engine  | 2026-03-14 12:22:47.671 | info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
workflow-engine  | 2026-03-14 12:22:47.671 |       Start processing HTTP request PUT http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files/content?*
workflow-engine  | 2026-03-14 12:22:47.671 | info: System.Net.Http.HttpClient.Default.ClientHandler[100]
workflow-engine  | 2026-03-14 12:22:47.671 |       Sending HTTP request PUT http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files/content?*
workflow-engine  | 2026-03-14 12:22:47.672 | info: System.Net.Http.HttpClient.Default.ClientHandler[101]
workflow-engine  | 2026-03-14 12:22:47.673 |       Received HTTP response headers after 1.0931ms - 200
workflow-engine  | 2026-03-14 12:22:47.673 | info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
workflow-engine  | 2026-03-14 12:22:47.673 |       End processing HTTP request after 1.2177ms - 200
workflow-engine  | 2026-03-14 12:22:48.751 | info: ReelForge.WorkflowEngine.Agents.Tools.ReactRemotionSandboxTools[0]
workflow-engine  | 2026-03-14 12:22:48.751 |       Running typecheck/lint in sandbox for execution 5227a5ab-ab88-4405-8310-819fe90a494b
workflow-engine  | 2026-03-14 12:22:48.751 | info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
workflow-engine  | 2026-03-14 12:22:48.751 |       Start processing HTTP request POST http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/exec
workflow-engine  | 2026-03-14 12:22:48.751 | info: System.Net.Http.HttpClient.Default.ClientHandler[100]
workflow-engine  | 2026-03-14 12:22:48.751 |       Sending HTTP request POST http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/exec
sandbox-executor | 2026-03-14 12:22:48.751 | 2026/03/14 11:22:48 [docker] running: docker exec rf-sbx-fbb66437-961f-49f0-b695-c6a496daa3ec sh -c mkdir -p /workspace/node_modules/.remotion/chrome-headless-shell/linux64/chrome-headless-shell-linux64/ && if [ ! -x /workspace/node_modules/.remotion/chrome-headless-shell/linux64/chrome-headless-shell-linux64/chrome-headless-shell ]; then ln -sf /usr/bin/chromium /workspace/node_modules/.remotion/chrome-headless-shell/linux64/chrome-headless-shell-linux64/chrome-headless-shell ; fi || true
sandbox-executor | 2026-03-14 12:22:48.841 | 2026/03/14 11:22:48 exec request for workflow 5227a5ab-ab88-4405-8310-819fe90a494b: npm [run typecheck]
sandbox-executor | 2026-03-14 12:22:48.842 | 2026/03/14 11:22:48 [docker] running: docker exec -w /workspace rf-sbx-fbb66437-961f-49f0-b695-c6a496daa3ec npm run typecheck
sandbox-executor | 2026-03-14 12:22:50.625 | 2026/03/14 11:22:50 [docker] output: 
sandbox-executor | 2026-03-14 12:22:50.625 | > typecheck
sandbox-executor | 2026-03-14 12:22:50.625 | > tsc --noEmit
sandbox-executor | 2026-03-14 12:22:50.625 | 
sandbox-executor | 2026-03-14 12:22:50.625 | src/index.ts(4,22): error TS5097: An import path can only end with a '.tsx' extension when 'allowImportingTsExtensions' is enabled.
sandbox-executor | 2026-03-14 12:22:50.625 | src/root.tsx(4,34): error TS2307: Cannot find module './WorkflowStepList.tsx' or its corresponding type declarations.
sandbox-executor | 2026-03-14 12:22:50.625 | src/root.tsx(5,26): error TS2307: Cannot find module './StepCard.tsx' or its corresponding type declarations.
sandbox-executor | 2026-03-14 12:22:50.625 | src/root.tsx(6,41): error TS2307: Cannot find module './FlowchartBuilder.tsx' or its corresponding type declarations.
sandbox-executor | 2026-03-14 12:22:50.625 | src/root.tsx(7,35): error TS2307: Cannot find module './ExecutionProgress.tsx' or its corresponding type declarations.
sandbox-executor | 2026-03-14 12:22:50.625 | src/root.tsx(8,34): error TS2307: Cannot find module './ExecutionHistory.tsx' or its corresponding type declarations.
sandbox-executor | 2026-03-14 12:22:50.625 | 2026/03/14 11:22:50 [docker] error: exit status 2
sandbox-executor | 2026-03-14 12:22:50.625 | 2026/03/14 11:22:50 exec failed for workflow 5227a5ab-ab88-4405-8310-819fe90a494b: exit status 2; output: 
sandbox-executor | 2026-03-14 12:22:50.625 | > typecheck
sandbox-executor | 2026-03-14 12:22:50.625 | > tsc --noEmit
sandbox-executor | 2026-03-14 12:22:50.625 | 
sandbox-executor | 2026-03-14 12:22:50.625 | src/index.ts(4,22): error TS5097: An import path can only end with a '.tsx' extension when 'allowImportingTsExtensions' is enabled.
sandbox-executor | 2026-03-14 12:22:50.625 | src/root.tsx(4,34): error TS2307: Cannot find module './WorkflowStepList.tsx' or its corresponding type declarations.
sandbox-executor | 2026-03-14 12:22:50.625 | src/root.tsx(5,26): error TS2307: Cannot find module './StepCard.tsx' or its corresponding type declarations.
sandbox-executor | 2026-03-14 12:22:50.625 | src/root.tsx(6,41): error TS2307: Cannot find module './FlowchartBuilder.tsx' or its corresponding type declarations.
sandbox-executor | 2026-03-14 12:22:50.625 | src/root.tsx(7,35): error TS2307: Cannot find module './ExecutionProgress.tsx' or its corresponding type declarations.
sandbox-executor | 2026-03-14 12:22:50.625 | src/root.tsx(8,34): error TS2307: Cannot find module './ExecutionHistory.tsx' or its corresponding type declarations.
workflow-engine  | 2026-03-14 12:22:50.626 | info: System.Net.Http.HttpClient.Default.ClientHandler[101]
workflow-engine  | 2026-03-14 12:22:50.626 |       Received HTTP response headers after 1874.7781ms - 500
workflow-engine  | 2026-03-14 12:22:50.626 | info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
workflow-engine  | 2026-03-14 12:22:50.626 |       End processing HTTP request after 1874.8565ms - 500
workflow-engine  | 2026-03-14 12:22:50.626 | info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
workflow-engine  | 2026-03-14 12:22:50.626 |       Start processing HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files/content?*
workflow-engine  | 2026-03-14 12:22:50.626 | info: System.Net.Http.HttpClient.Default.ClientHandler[100]
workflow-engine  | 2026-03-14 12:22:50.626 |       Sending HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files/content?*
workflow-engine  | 2026-03-14 12:22:50.627 | info: System.Net.Http.HttpClient.Default.ClientHandler[101]
workflow-engine  | 2026-03-14 12:22:50.627 |       Received HTTP response headers after 0.7289ms - 200
workflow-engine  | 2026-03-14 12:22:50.627 | info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
workflow-engine  | 2026-03-14 12:22:50.627 |       End processing HTTP request after 0.8609ms - 200
workflow-engine  | 2026-03-14 12:22:51.818 | info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
workflow-engine  | 2026-03-14 12:22:51.819 |       Start processing HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files?*
workflow-engine  | 2026-03-14 12:22:51.819 | info: System.Net.Http.HttpClient.Default.ClientHandler[100]
workflow-engine  | 2026-03-14 12:22:51.819 |       Sending HTTP request GET http://sandbox-executor:8080/api/v1/sandboxes/5227a5ab-ab88-4405-8310-819fe90a494b/files?*
workflow-engine  | 2026-03-14 12:22:51.819 | info: System.Net.Http.HttpClient.Default.ClientHandler[101]
workflow-engine  | 2026-03-14 12:22:51.819 |       Received HTTP response headers after 0.6534ms - 200
workflow-engine  | 2026-03-14 12:22:51.819 | info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
workflow-engine  | 2026-03-14 12:22:51.819 |       End processing HTTP request after 0.8969ms - 200
workflow-engine  | 2026-03-14 12:23:00.161 | info: ReelForge.WorkflowEngine.Execution.WorkflowExecutorService[0]
workflow-engine  | 2026-03-14 12:23:00.161 |       Step 3 (Agent) completed for execution 5227a5ab-ab88-4405-8310-819fe90a494b with status Completed; duration 87213ms; tokens 747651; nextStepIndex 3
workflow-engine  | 2026-03-14 12:23:00.170 | info: ReelForge.WorkflowEngine.Execution.WorkflowEventPublisher[0]
workflow-engine  | 2026-03-14 12:23:00.170 |       Publishing step completed event: ExecutionId=5227a5ab-ab88-4405-8310-819fe90a494b, StepId=b060e5e6-b3d9-410f-a89b-ee25fae74d36, Status=Completed
workflow-engine  | 2026-03-14 12:23:00.179 | info: ReelForge.WorkflowEngine.Execution.WorkflowEventPublisher[0]
workflow-engine  | 2026-03-14 12:23:00.179 |       Publishing execution completed event: ExecutionId=5227a5ab-ab88-4405-8310-819fe90a494b, FinalStatus=Passed
workflow-engine  | 2026-03-14 12:23:00.196 | info: ReelForge.WorkflowEngine.Execution.WorkflowExecutorService[0]
workflow-engine  | 2026-03-14 12:23:00.196 |       Workflow execution 5227a5ab-ab88-4405-8310-819fe90a494b completed successfully
workflow-engine  | 2026-03-14 12:23:00.196 | info: ReelForge.WorkflowEngine.Consumers.WorkflowExecutionRequestedConsumer[0]
workflow-engine  | 2026-03-14 12:23:00.196 |       Workflow execution request processed: ExecutionId=5227a5ab-ab88-4405-8310-819fe90a494b, CorrelationId=cde0617e-63c6-45ea-b469-42adc569e692
inference        | 2026-03-14 12:25:16.704 | fail: Microsoft.AspNetCore.Diagnostics.DeveloperExceptionPageMiddleware[1]
inference        | 2026-03-14 12:25:16.704 |       An unhandled exception has occurred while executing the request.
inference        | 2026-03-14 12:25:16.704 |       Amazon.S3.Model.NoSuchKeyException: The specified key does not exist.
inference        | 2026-03-14 12:25:16.704 |        ---> Amazon.Runtime.Internal.HttpErrorResponseException: Exception of type 'Amazon.Runtime.Internal.HttpErrorResponseException' was thrown.
inference        | 2026-03-14 12:25:16.704 |          at Amazon.Runtime.HttpWebRequestMessage.ProcessHttpResponseMessage(HttpResponseMessage responseMessage)
inference        | 2026-03-14 12:25:16.704 |          at Amazon.Runtime.HttpWebRequestMessage.GetResponseAsync(CancellationToken cancellationToken)
inference        | 2026-03-14 12:25:16.704 |          at Amazon.Runtime.Internal.HttpHandler`1.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:25:16.704 |          at Amazon.Runtime.Internal.RedirectHandler.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:25:16.704 |          at Amazon.Runtime.Internal.Unmarshaller.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:25:16.704 |          at Amazon.S3.Internal.AmazonS3ResponseHandler.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:25:16.704 |          at Amazon.Runtime.Internal.ErrorHandler.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:25:16.704 |          --- End of inner exception stack trace ---
inference        | 2026-03-14 12:25:16.704 |          at Amazon.Runtime.Internal.HttpErrorResponseExceptionHandler.HandleExceptionStream(IRequestContext requestContext, IWebResponseData httpErrorResponse, HttpErrorResponseException exception, Stream responseStream)
inference        | 2026-03-14 12:25:16.704 |          at Amazon.Runtime.Internal.HttpErrorResponseExceptionHandler.HandleExceptionAsync(IExecutionContext executionContext, HttpErrorResponseException exception)
inference        | 2026-03-14 12:25:16.704 |          at Amazon.Runtime.Internal.ExceptionHandler`1.HandleAsync(IExecutionContext executionContext, Exception exception)
inference        | 2026-03-14 12:25:16.704 |          at Amazon.Runtime.Internal.ErrorHandler.ProcessExceptionAsync(IExecutionContext executionContext, Exception exception)
inference        | 2026-03-14 12:25:16.704 |          at Amazon.Runtime.Internal.ErrorHandler.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:25:16.704 |          at Amazon.Runtime.Internal.CallbackHandler.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:25:16.704 |          at Amazon.Runtime.Internal.Signer.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:25:16.704 |          at Amazon.S3.Internal.S3Express.S3ExpressPreSigner.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:25:16.704 |          at Amazon.Runtime.Internal.EndpointDiscoveryHandler.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:25:16.704 |          at Amazon.Runtime.Internal.EndpointDiscoveryHandler.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:25:16.704 |          at Amazon.Runtime.Internal.RetryHandler.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:25:16.704 |          at Amazon.Runtime.Internal.RetryHandler.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:25:16.704 |          at Amazon.Runtime.Internal.CallbackHandler.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:25:16.704 |          at Amazon.Runtime.Internal.BaseAuthResolverHandler.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:25:16.704 |          at Amazon.Runtime.Internal.CallbackHandler.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:25:16.704 |          at Amazon.S3.Internal.AmazonS3ExceptionHandler.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:25:16.704 |          at Amazon.Runtime.Internal.ErrorCallbackHandler.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:25:16.704 |          at Amazon.Runtime.Internal.MetricsHandler.InvokeAsync[T](IExecutionContext executionContext)
inference        | 2026-03-14 12:25:16.704 |          at ReelForge.Inference.Api.Controllers.OutputsController.DownloadOutput(Guid projectId, Guid stepResultId, CancellationToken ct) in /src/src/ReelForge.Inference.Api/Controllers/OutputsController.cs:line 93
inference        | 2026-03-14 12:25:16.704 |          at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.TaskOfIActionResultExecutor.Execute(ActionContext actionContext, IActionResultTypeMapper mapper, ObjectMethodExecutor executor, Object controller, Object[] arguments)
inference        | 2026-03-14 12:25:16.704 |          at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeActionMethodAsync>g__Awaited|12_0(ControllerActionInvoker invoker, ValueTask`1 actionResultValueTask)
inference        | 2026-03-14 12:25:16.704 |          at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeNextActionFilterAsync>g__Awaited|10_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)
inference        | 2026-03-14 12:25:16.704 |          at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Rethrow(ActionExecutedContextSealed context)
inference        | 2026-03-14 12:25:16.704 |          at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Next(State& next, Scope& scope, Object& state, Boolean& isCompleted)
inference        | 2026-03-14 12:25:16.704 |          at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeInnerFilterAsync>g__Awaited|13_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)
inference        | 2026-03-14 12:25:16.704 |          at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeFilterPipelineAsync>g__Awaited|20_0(ResourceInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)
inference        | 2026-03-14 12:25:16.705 |          at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)
inference        | 2026-03-14 12:25:16.705 |          at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)
inference        | 2026-03-14 12:25:16.705 |          at Microsoft.AspNetCore.Authorization.AuthorizationMiddleware.Invoke(HttpContext context)
inference        | 2026-03-14 12:25:16.705 |          at Microsoft.AspNetCore.Authentication.AuthenticationMiddleware.Invoke(HttpContext context)
inference        | 2026-03-14 12:25:16.705 |          at Swashbuckle.AspNetCore.SwaggerUI.SwaggerUIMiddleware.Invoke(HttpContext httpContext)
inference        | 2026-03-14 12:25:16.705 |          at Swashbuckle.AspNetCore.Swagger.SwaggerMiddleware.Invoke(HttpContext httpContext, ISwaggerProvider swaggerProvider)
inference        | 2026-03-14 12:25:16.705 |          at Microsoft.AspNetCore.Diagnostics.DeveloperExceptionPageMiddlewareImpl.Invoke(HttpContext context)