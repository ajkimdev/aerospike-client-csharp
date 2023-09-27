/* 
 * Copyright 2012-2020 Aerospike, Inc.
 *
 * Portions may be licensed to Aerospike, Inc. under one or more contributor
 * license agreements.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy of
 * the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations under
 * the License.
 */
using Aerospike.Client.KVS;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using System;
using System.Collections.Generic;

namespace Aerospike.Client
{
	public sealed class ReadHeaderCommandProxy : GRPCCommand
	{
		private Record record;

		public ReadHeaderCommandProxy(Buffer buffer, CallInvoker invoker, Policy policy, Key key)
			: base(buffer, invoker, policy, key)
		{
		}

		protected internal override void WriteBuffer()
		{
			SetReadHeader(policy, key);
		}

		protected internal override bool ParseRow()
		{
			throw new AerospikeException(NotSupported + "ParseRow");
		}

		protected internal override void ParseResult(IConnection conn)
		{
			// Read header.		
			conn.ReadFully(Buffer.DataBuffer, MSG_TOTAL_HEADER_SIZE);
			conn.UpdateLastUsed();

			int resultCode = Buffer.DataBuffer[13];

			if (resultCode == 0)
			{
				int generation = ByteUtil.BytesToInt(Buffer.DataBuffer, 14);
				int expiration = ByteUtil.BytesToInt(Buffer.DataBuffer, 18);
				record = new Record(null, generation, expiration);
				return;
			}

			if (resultCode == ResultCode.KEY_NOT_FOUND_ERROR)
			{
				return;
			}

			if (resultCode == ResultCode.FILTERED_OUT)
			{
				if (policy.failOnFilteredOut)
				{
					throw new AerospikeException(resultCode);
				}
				return;
			}

			throw new AerospikeException(resultCode);	
		}

		public Record Record
		{
			get
			{
				return record;
			}
		}

		public void Execute()
		{
			WriteBuffer();
			var request = new AerospikeRequestPayload
			{
				Id = 0, // ID is only needed in streaming version, can be static for unary
				Iteration = 1,
				Payload = ByteString.CopyFrom(Buffer.DataBuffer, 0, Buffer.Offset)
			};
			GRPCConversions.SetRequestPolicy(policy, request);

			try
			{
				var client = new KVS.KVS.KVSClient(CallInvoker);
				var deadline = DateTime.UtcNow.AddMilliseconds(totalTimeout);
				var response = client.GetHeader(request, deadline: deadline);
				var conn = new ConnectionProxy(response);
				ParseResult(conn);
			}
			catch (RpcException e)
			{
				throw GRPCConversions.ToAerospikeException(e, totalTimeout, true);
			}
		}

		public async Task<Record> Execute(CancellationToken token)
		{
			WriteBuffer();
			var request = new AerospikeRequestPayload
			{
				Id = 0, // ID is only needed in streaming version, can be static for unary
				Iteration = 1,
				Payload = ByteString.CopyFrom(Buffer.DataBuffer, 0, Buffer.Offset)
			};
			GRPCConversions.SetRequestPolicy(policy, request);

			try
			{
				var client = new KVS.KVS.KVSClient(CallInvoker);
				var deadline = DateTime.UtcNow.AddMilliseconds(totalTimeout);
				var response = await client.GetHeaderAsync(request, deadline: deadline, cancellationToken: token);
				var conn = new ConnectionProxy(response);
				ParseResult(conn);
				return record;
			}
			catch (RpcException e)
			{
				throw GRPCConversions.ToAerospikeException(e, totalTimeout, true);
			}
		}
	}
}
