////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Optional, reflection-only bridge to the A* Pathfinding Project shipped with
	/// Unturned. The public SDK intentionally omits the licensed ASPFP implementation,
	/// so this component must not have a compile-time dependency on Pathfinding types.
	/// It requests routes from the active A* graph and leaves physical movement to the
	/// humanoid motor's CharacterController.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class MobileNPCNativeAStarRouteProvider : MonoBehaviour
	{
		private Component seeker;
		private MethodInfo startPathMethod;
		private MethodInfo cancelPathMethod;
		private Delegate pathCallback;
		private int requestVersion;
		private int activeRequestVersion;
		private bool initialized;
		private float nextInitializationAttemptTime;
		private string diagnosticStatus = "waiting-for-backend";
		private bool requestPending;
		private bool routeReady;
		private bool lastPathFailed;
		private float nextRequestTime;
		private float requestStartedAt;
		private Vector3 requestedGoal;
		private readonly List<Vector3> completedRoute = new List<Vector3>(32);

		public bool IsAvailable
		{
			get
			{
				EnsureInitialized();
				return seeker != null && startPathMethod != null;
			}
		}

		public bool IsRequestPending => requestPending;
		public bool LastPathFailed => lastPathFailed;
		/// <summary>Compact runtime state for debugging without per-frame log spam.</summary>
		public string DiagnosticStatus => diagnosticStatus;

		/// <summary>
		/// Returns true only when a completed native A* route is ready for consumption.
		/// Otherwise it starts an asynchronous request when possible and returns false.
		/// </summary>
		public bool TryGetRoute(Vector3 start, Vector3 goal, List<Vector3> output)
		{
			if (output == null)
				return false;

			EnsureInitialized();
			if (!IsAvailable)
				return false;

			if (routeReady)
			{
				if ((goal - requestedGoal).sqrMagnitude <= 9f && completedRoute.Count >= 2)
				{
					output.Clear();
					output.AddRange(completedRoute);
					routeReady = false;
					return true;
				}

				routeReady = false;
				completedRoute.Clear();
			}

			if (requestPending)
			{
				if (Time.time - requestStartedAt > 4.0f)
				{
					CancelPendingRequest();
					lastPathFailed = true;
					diagnosticStatus = "request-timeout";
					nextRequestTime = Time.time + 2.5f;
				}
				else if ((goal - requestedGoal).sqrMagnitude <= 9f)
				{
					return false;
				}
				else
				{
					CancelPendingRequest();
				}
			}

			if (Time.time < nextRequestTime)
				return false;

			requestedGoal = goal;
			lastPathFailed = false;
			activeRequestVersion = ++requestVersion;
			pathCallback = CreatePathCallback(activeRequestVersion);
			if (pathCallback == null)
			{
				lastPathFailed = true;
				diagnosticStatus = "callback-generation-failed";
				nextRequestTime = Time.time + 2.5f;
				return false;
			}
			completedRoute.Clear();
			requestPending = true;
			requestStartedAt = Time.time;
			try
			{
				ParameterInfo[] parameters = startPathMethod.GetParameters();
				object[] args = new object[parameters.Length];
				args[0] = start;
				args[1] = goal;
				args[2] = pathCallback;
				for (int i = 3; i < parameters.Length; ++i)
				{
					if (parameters[i].ParameterType == typeof(int))
						args[i] = -1;
					else if (parameters[i].HasDefaultValue)
						args[i] = parameters[i].DefaultValue;
					else
						args[i] = parameters[i].ParameterType.IsValueType
							? Activator.CreateInstance(parameters[i].ParameterType)
							: null;
				}

				startPathMethod.Invoke(seeker, args);
				diagnosticStatus = "request-pending";
				return false;
			}
			catch (Exception)
			{
				requestPending = false;
				lastPathFailed = true;
				diagnosticStatus = "startpath-invocation-failed";
				nextRequestTime = Time.time + 2.5f;
				return false;
			}
		}

		private void EnsureInitialized()
		{
			if (initialized || Time.time < nextInitializationAttemptTime)
				return;
			nextInitializationAttemptTime = Time.time + 2.0f;

			// Do not attach a generic Seeker to an unrelated or empty A* instance.
			// Unturned selects this backend only in builds that include the licensed
			// ASPFP integration; the public SDK otherwise selects UnturnedPathfinding_Empty.
			IUnturnedPathfindingInterface activeBackend = UnturnedPathfinding.Get();
			if (activeBackend == null)
				return;
			if (activeBackend.GetType().Name != "UnturnedPathfinding_ASPFP")
			{
				diagnosticStatus = "native-aspfp-backend-unavailable";
				initialized = true;
				return;
			}

			Type seekerType = FindType("Pathfinding.Seeker");
			if (seekerType == null || !typeof(Component).IsAssignableFrom(seekerType))
			{
				diagnosticStatus = "aspfp-active-seeker-type-not-found";
				return;
			}

			try
			{
				seeker = GetComponent(seekerType);
				if (seeker == null)
					seeker = gameObject.AddComponent(seekerType);

				MethodInfo[] methods = seekerType.GetMethods(BindingFlags.Instance | BindingFlags.Public);
				for (int i = 0; i < methods.Length; ++i)
				{
					MethodInfo method = methods[i];
					ParameterInfo[] parameters = method.GetParameters();
					if (method.Name == "CancelCurrentPathRequest" && parameters.Length == 0)
					{
						cancelPathMethod = method;
						continue;
					}

					if (method.Name != "StartPath" || parameters.Length < 3
						|| parameters[0].ParameterType != typeof(Vector3)
						|| parameters[1].ParameterType != typeof(Vector3)
						|| !typeof(Delegate).IsAssignableFrom(parameters[2].ParameterType))
						continue;

					MethodInfo invoke = parameters[2].ParameterType.GetMethod("Invoke");
					if (invoke == null || invoke.ReturnType != typeof(void)
						|| invoke.GetParameters().Length != 1)
						continue;

					startPathMethod = method;
					break;
				}

				if (startPathMethod == null)
				{
					diagnosticStatus = "aspfp-seeker-startpath-signature-not-found";
					return;
				}

				initialized = true;
				diagnosticStatus = "ready";
			}
			catch (Exception)
			{
				seeker = null;
				startPathMethod = null;
				pathCallback = null;
				diagnosticStatus = "aspfp-reflection-initialization-failed";
			}
		}

		private Delegate CreatePathCallback(int version)
		{
			try
			{
				if (startPathMethod == null)
					return null;
				Type callbackType = startPathMethod.GetParameters()[2].ParameterType;
				MethodInfo callbackTarget = GetType().GetMethod(
					nameof(OnNativePathComplete), BindingFlags.Instance | BindingFlags.NonPublic);
				ParameterInfo[] callbackParameters = callbackType.GetMethod("Invoke").GetParameters();
				if (callbackParameters.Length != 1)
					return null;
				ParameterExpression callbackArgument = Expression.Parameter(callbackParameters[0].ParameterType, "path");
				MethodCallExpression callbackBody = Expression.Call(
					Expression.Constant(this), callbackTarget,
					Expression.Convert(callbackArgument, typeof(object)),
					Expression.Constant(version));
				return Expression.Lambda(callbackType, callbackBody, callbackArgument).Compile();
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static Type FindType(string fullName)
		{
			Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
			for (int i = 0; i < assemblies.Length; ++i)
			{
				Type type = assemblies[i].GetType(fullName, false);
				if (type != null)
					return type;
			}

			return null;
		}

		private void OnNativePathComplete(object path, int version)
		{
			if (version != activeRequestVersion || !requestPending)
				return;
			requestPending = false;
			routeReady = false;
			completedRoute.Clear();
			if (path == null)
			{
				lastPathFailed = true;
				diagnosticStatus = "null-path-callback";
				nextRequestTime = Time.time + 2.5f;
				return;
			}

			try
			{
				Type pathType = path.GetType();
				PropertyInfo errorProperty = pathType.GetProperty("error", BindingFlags.Instance | BindingFlags.Public);
				if (errorProperty != null && errorProperty.PropertyType == typeof(bool)
					&& (bool)errorProperty.GetValue(path, null))
				{
					lastPathFailed = true;
					diagnosticStatus = "path-error";
					nextRequestTime = Time.time + 2.5f;
					return;
				}

				PropertyInfo vectorPathProperty = pathType.GetProperty("vectorPath", BindingFlags.Instance | BindingFlags.Public);
				IEnumerable points = vectorPathProperty != null
					? vectorPathProperty.GetValue(path, null) as IEnumerable
					: null;
				if (points == null)
				{
					lastPathFailed = true;
					diagnosticStatus = "vector-path-unavailable";
					nextRequestTime = Time.time + 2.5f;
					return;
				}

				foreach (object point in points)
				{
					if (point is Vector3 position)
						completedRoute.Add(position);
				}

				routeReady = completedRoute.Count >= 2;
				lastPathFailed = !routeReady;
				diagnosticStatus = routeReady ? "route-ready" : "empty-route";
				if (lastPathFailed)
					nextRequestTime = Time.time + 2.5f;
			}
			catch (Exception)
			{
				completedRoute.Clear();
				routeReady = false;
				lastPathFailed = true;
				diagnosticStatus = "path-processing-failed";
				nextRequestTime = Time.time + 2.5f;
			}
		}

		private void CancelPendingRequest()
		{
			try
			{
				if (seeker != null && cancelPathMethod != null)
					cancelPathMethod.Invoke(seeker, null);
			}
			catch (Exception)
			{
				// The provider remains optional; a failed cancellation must not break motor fallback.
			}

			requestPending = false;
			activeRequestVersion = ++requestVersion;
		}

		private void OnDestroy()
		{
			CancelPendingRequest();
		}
	}
}
