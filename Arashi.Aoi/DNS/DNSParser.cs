﻿using System;
using System.Buffers;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using ARSoft.Tools.Net;
using ARSoft.Tools.Net.Dns;
using Microsoft.AspNetCore.Http;
using static Arashi.AoiConfig;

namespace Arashi
{
    class DNSParser
    {
        public static DnsMessage FromDnsJson(HttpContext context, bool ActiveEcs = true, byte EcsDefaultMask = 24)
        {
            var queryDictionary = context.Request.Query;
            var dnsQuestion = new DnsQuestion(DomainName.Parse(queryDictionary["name"]),
                context.Connection.RemoteIpAddress!.AddressFamily == AddressFamily.InterNetworkV6 ? RecordType.Aaaa : RecordType.A,
                RecordClass.INet);
            if (queryDictionary.ContainsKey("type"))
                if (Enum.TryParse(queryDictionary["type"], true, out RecordType rType))
                    dnsQuestion = new DnsQuestion(DomainName.Parse(queryDictionary["name"]), rType,
                        RecordClass.INet);

            var dnsQMsg = new DnsMessage
            {
                IsEDnsEnabled = true,
                IsQuery = true,
                IsRecursionAllowed = true,
                IsRecursionDesired = true,
                TransactionID = Convert.ToUInt16(new Random(DateTime.Now.Millisecond).Next(1, 99))
            };
            dnsQMsg.Questions.Add(dnsQuestion);
            if (!Config.EcsEnable || !ActiveEcs || queryDictionary.ContainsKey("no-ecs")) return dnsQMsg;

            if (queryDictionary.ContainsKey("edns_client_subnet"))
            {
                var ipStr = queryDictionary["edns_client_subnet"].ToString();
                var ipNetwork = ipStr.Contains("/")
                    ? IPNetwork2.Parse(ipStr)
                    : IPNetwork2.Parse(ipStr, EcsDefaultMask);
                dnsQMsg.EDnsOptions.Options.Add(new ClientSubnetOption(
                    Equals(ipNetwork.Network, IPAddress.Any) ? (byte)0 : ipNetwork.Cidr, ipNetwork.Network));
            }
            else
            {
                var realIp = context.Connection.RemoteIpAddress;
                if (!Equals(realIp, IPAddress.Loopback))
                {
                    dnsQMsg.EDnsOptions.Options.Add(
                        new ClientSubnetOption(EcsDefaultMask,
                            IPNetwork2.Parse(realIp.ToString(), EcsDefaultMask).Network));
                }
            }

            return dnsQMsg;
        }

        public static DnsMessage FromWebBase64(string base64) => DnsMessage.Parse(DecodeWebBase64(base64));

        public static DnsMessage FromWebBase64(HttpContext context, bool ActiveEcs = true,
            byte EcsDefaultMask = 24, byte EcsV6DefaultMask = 48)

        {
            var msg = FromWebBase64(context.Request.Query["dns"].ToString());
            if (!Config.EcsEnable || !ActiveEcs || context.Request.Query.ContainsKey("no-ecs")) return msg;
            if (IsEcsEnable(msg)) return msg;
            if (!msg.IsEDnsEnabled) msg.IsEDnsEnabled = true;
            var mask = context.Connection.RemoteIpAddress.AddressFamily == AddressFamily.InterNetworkV6
                ? EcsV6DefaultMask : EcsDefaultMask;
            msg.EDnsOptions.Options.Add(new ClientSubnetOption(mask,
                IPNetwork2.Parse(context.Connection.RemoteIpAddress.ToString(), mask).Network));
            msg.EDnsOptions.Options.RemoveAll(x => x.Type == EDnsOptionType.Padding);
            return msg;
        }

        public static async Task<DnsMessage> FromPostByteAsync(HttpContext context, bool ActiveEcs = true,
            byte EcsDefaultMask = 24, byte EcsV6DefaultMask = 48)
        {
            var msg = DnsMessage.Parse((await context.Request.BodyReader.ReadAsync()).Buffer.ToArray());
            if (!Config.EcsEnable || !ActiveEcs || context.Request.Query.ContainsKey("no-ecs")) return msg;
            if (IsEcsEnable(msg)) return msg;
            if (!msg.IsEDnsEnabled) msg.IsEDnsEnabled = true;
            var mask = context.Connection.RemoteIpAddress.AddressFamily == AddressFamily.InterNetworkV6
                ? EcsV6DefaultMask : EcsDefaultMask;
            msg.EDnsOptions.Options.Add(new ClientSubnetOption(mask,
                IPNetwork2.Parse(context.Connection.RemoteIpAddress.ToString(), EcsDefaultMask).Network));
            msg.EDnsOptions.Options.RemoveAll(x => x.Type == EDnsOptionType.Padding);
            return msg;
        }

        public static bool IsEcsEnable(DnsMessage msg)
        {
            return msg.IsEDnsEnabled && msg.EDnsOptions.Options.ToArray().OfType<ClientSubnetOption>().Any();
        }
        public static byte[] DecodeWebBase64(string base64)
        {
            if (base64.Length % 4 > 0) base64 = base64.PadRight(base64.Length + 4 - base64.Length % 4, '=');
            return Convert.FromBase64String(base64.Replace("-", "+").Replace("_", "/"));
        }
    }
}
