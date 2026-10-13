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
		private bool initialized;
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
				return seeker != null && startPathMethod != null && pathCallback != null;
			}
		}

		public bool IsRequestPending => requestPending;
		public bool LastPathFailed => lastPathFailed;

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
				return false;
			}
			catch (Exception)
			{
				requestPending = false;
				lastPathFailed = true;
				nextRequestTime = Time.time + 2.5f;
				return false;
			}
		}

		private void EnsureInitialized()
		{
			if (initialized)
				return;
			initialized = true;

			Type seekerType = FindType("Pathfinding.Seeker");
			if (seekerType == null || !typeof(Component).IsAssignableFrom(seekerType))
				return;

			try
			{
				seeker = GetComponent(seekerType);
				if (seeker == null)
					seeker = gameObject.AddComponent(seekerType);

				MethodInfo[] methods = seekerType.GetMethods(BindingFlags.Instance | BindingFlags.Public);
				for (int i = 0; i < methods.Length; ++i)
				{
					MethodInfo method = methods[i];
					if (method.Name == "StartPath" && method.GetParameters().Length >= 3)
					{
						ParameterInfo[] parameters = method.GetParameters();
						if (parameters[0].ParameterType == typeof(Vector3)
							&& parameters[1].ParameterType == typeof(Vector3)
							&& typeof(Delegate).IsAssignableFrom(parameters[2].ParameterType))
						{
							startPathMethod = method;
							break;
						}
					}
					else if (method.Name == "CancelCurrentPathRequest" && method.GetParameters().Length == 0)
					{
						cancelPathMethod = method;
					}
				}

				if (startPathMethod == null)
					return;

				Type callbackType = startPathMethod.GetParameters()[2].ParameterType;
				MethodInfo callbackTarget = GetType().GetMethod(
					nameof(OnNativePathComplete), BindingFlags.Instance | BindingFlags.NonPublic);
				ParameterInfo[] callbackParameters = callbackType.GetMethod("Invoke").GetParameters();
				if (callbackParameters.Length != 1)
					return;

				ParameterExpression callbackArgument = Expression.Parameter(callbackParameters[0].ParameterType, "path");
				MethodCallExpression callbackBody = Expression.Call(
					Expression.Constant(this), callbackTarget, Expression.Convert(callbackArgument, typeof(object)));
				pathCallback = Expression.Lambda(callbackType, callbackBody, callbackArgument).Compile();
			}
			catch (Exception)
			{
				seeker = null;
				startPathMethod = null;
				pathCallback = null;
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

		private void OnNativePathComplete(object path)
		{
			requestPending = false;
			routeReady = false;
			completedRoute.Clear();
			if (path == null)
			{
				lastPathFailed = true;
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
				if (lastPathFailed)
					nextRequestTime = Time.time + 2.5f;
			}
			catch (Exception)
			{
				completedRoute.Clear();
				routeReady = false;
				lastPathFailed = true;
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
		}

		private void OnDestroy()
		{
			CancelPendingRequest();
		}
	}
}
