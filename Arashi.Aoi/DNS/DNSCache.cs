﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.Caching;
using ARSoft.Tools.Net;
using ARSoft.Tools.Net.Dns;
using Microsoft.AspNetCore.Http;

namespace Arashi
{
    public class DnsCache
    {
        public static void Add(DnsMessage dnsMessage, string tag = "")
        {
            if (dnsMessage.ReturnCode != ReturnCode.NoError && dnsMessage.ReturnCode != ReturnCode.NxDomain) return;
            var record = dnsMessage.AnswerRecords.FirstOrDefault() ?? new ARecord(DomainName.Root,
                dnsMessage.ReturnCode == ReturnCode.NoError ? 180 : 90, IPAddress.Any);
            var quest = dnsMessage.Questions.First();

            var ttl = dnsMessage.AnswerRecords.Select(x => x.TimeToLive).Min();
            if (record.TimeToLive < AoiConfig.Config.MinTTL) return;
            if (record.TimeToLive >= AoiConfig.Config.MaxTTL)
                ttl = AoiConfig.Config.TargetTTL;

            AddForce(new CacheItem($"DNS:{quest.Name.ToString().ToLower()}:{quest.RecordType}:{tag}",
                new CacheEntity
                {
                    AnswerRecords = dnsMessage.AnswerRecords.Take(6).ToList(),
                    AuthorityRecords = dnsMessage.AuthorityRecords.Where(x => x.RecordType != RecordType.Txt).ToList(),
                    Code = dnsMessage.ReturnCode,
                    Time = DateTime.Now,
                    ExpiresTime = DateTime.Now.AddSeconds(ttl)
                }), ttl);
        }

        public static void Add(DnsMessage dnsMessage, HttpContext context, string tag = "")
        {
            if (dnsMessage.ReturnCode != ReturnCode.NoError && dnsMessage.ReturnCode != ReturnCode.NxDomain) return;
            var record = dnsMessage.AnswerRecords.FirstOrDefault() ?? new ARecord(DomainName.Root,
                dnsMessage.ReturnCode == ReturnCode.NoError ? 180 : 90, IPAddress.Any);
            var quest = dnsMessage.Questions.First();

            var ttl = dnsMessage.AnswerRecords.Select(x => x.TimeToLive).Min();
            if (record.TimeToLive < 10) return;
            if (record.TimeToLive >= AoiConfig.Config.MaxTTL)
                ttl = AoiConfig.Config.TargetTTL;

            if (RealIP.TryGetFromDns(dnsMessage, out var ipAddress))
                AddForce(new CacheItem(
                    $"DNS:{GeoIP.GetGeoStr(ipAddress)}{quest.Name.ToString().ToLower()}:{quest.RecordType}:{tag}",
                    new CacheEntity
                    {
                        AnswerRecords = dnsMessage.AnswerRecords.Take(6).ToList(),
                        AuthorityRecords = dnsMessage.AuthorityRecords.Where(x => x.RecordType != RecordType.Txt)
                            .ToList(),
                        Code = dnsMessage.ReturnCode,
                        Time = DateTime.Now,
                        ExpiresTime = DateTime.Now.AddSeconds(ttl)
                    }), ttl);
            else
                AddForce(new CacheItem(
                    $"DNS:{GeoIP.GetGeoStr(context.Connection.RemoteIpAddress)}{quest.Name.ToString().ToLower()}:{quest.RecordType}:{tag}",
                    new CacheEntity
                    {
                        AnswerRecords = dnsMessage.AnswerRecords.Take(6).ToList(),
                        AuthorityRecords = dnsMessage.AuthorityRecords.Where(x => x.RecordType != RecordType.Txt)
                            .ToList(),
                        Code = dnsMessage.ReturnCode,
                        Time = DateTime.Now,
                        ExpiresTime = DateTime.Now.AddSeconds(ttl)
                    }), ttl);
        }

        public static void AddForce(CacheItem cacheItem, int ttl)
        {
            MemoryCache.Default.Add(cacheItem,
                new CacheItemPolicy
                {
                    AbsoluteExpiration =
                        DateTimeOffset.Now + TimeSpan.FromSeconds(ttl)
                });
        }

        public static void Add(CacheItem cacheItem, int ttl)
        {
            if (!MemoryCache.Default.Contains(cacheItem.Key))
                MemoryCache.Default.Add(cacheItem,
                    new CacheItemPolicy
                    {
                        AbsoluteExpiration =
                            DateTimeOffset.Now + TimeSpan.FromSeconds(ttl)
                    });
        }


        public static bool Contains(DnsMessage dnsQMsg, HttpContext context = null, string tag = "")
        {
            return context == null
                ? MemoryCache.Default.Contains(
                    $"DNS:{dnsQMsg.Questions.FirstOrDefault().Name.ToString().ToLower()}:{dnsQMsg.Questions.FirstOrDefault().RecordType}:{tag}")
                : MemoryCache.Default.Contains(
                    $"DNS:{GeoIP.GetGeoStr(RealIP.GetFromDns(dnsQMsg, context))}{dnsQMsg.Questions.FirstOrDefault().Name.ToString().ToLower()}:{dnsQMsg.Questions.FirstOrDefault().RecordType}:{tag}");
        }

        public static void Remove(DomainName name)
        {
            try
            {
                foreach (var item in MemoryCache.Default
                             .Where(item => item.Key.Contains(name.ToString().ToLower())).ToList())
                    MemoryCache.Default.Remove(item.Key);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        public static void Remove(DomainName name, RecordType type)
        {
            try
            {
                foreach (var item in MemoryCache.Default
                             .Where(item => item.Key.Contains($":{name.ToString().ToLower()}:{type}:")).ToList())
                    MemoryCache.Default.Remove(item.Key);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        public static void Remove(DomainName name, RecordType type, IPAddress ip)
        {
            try
            {
                MemoryCache.Default.Remove($"DNS:{name.ToString().ToLower()}:{type}:");
                //MemoryCache.Default.Remove($"DNS:{name.ToString().ToLower()}:{type}:");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            try
            {
                MemoryCache.Default.Remove($"DNS:{GeoIP.GetGeoStr(ip)}{name.ToString().ToLower()}:{type}:");
                //MemoryCache.Default.Remove($"DNS:{GeoIP.GetGeoStr(ip)}{name.ToString().ToLower()}:{type}:");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        public static DnsMessage Get(DnsMessage dnsQMessage, HttpContext context = null, string tag = "")
        {
            var dCacheMsg = new DnsMessage
            {
                IsRecursionAllowed = true,
                IsRecursionDesired = true,
                TransactionID = dnsQMessage.TransactionID
            };
            var getName = context != null
                ? $"DNS:{GeoIP.GetGeoStr(RealIP.GetFromDns(dnsQMessage, context))}{dnsQMessage.Questions.FirstOrDefault().Name.ToString().ToLower()}:{dnsQMessage.Questions.FirstOrDefault().RecordType}:{tag}"
                : $"DNS:{dnsQMessage.Questions.FirstOrDefault().Name.ToString().ToLower()}:{dnsQMessage.Questions.FirstOrDefault().RecordType}:{tag}";
            var cacheEntity = Get(getName);
            foreach (var item in cacheEntity.AnswerRecords)
            {
                if (item is ARecord aRecord)
                    dCacheMsg.AnswerRecords.Add(new ARecord(aRecord.Name,
                        Convert.ToInt32((cacheEntity.Time.AddSeconds(item.TimeToLive) - DateTime.Now)
                            .TotalSeconds), aRecord.Address));
                else if (item is AaaaRecord aaaaRecord)
                    dCacheMsg.AnswerRecords.Add(new AaaaRecord(aaaaRecord.Name,
                        Convert.ToInt32((cacheEntity.Time.AddSeconds(item.TimeToLive) - DateTime.Now)
                            .TotalSeconds), aaaaRecord.Address));
                else if (item is CNameRecord cNameRecord)
                    dCacheMsg.AnswerRecords.Add(new CNameRecord(cNameRecord.Name,
                        Convert.ToInt32((cacheEntity.Time.AddSeconds(item.TimeToLive) - DateTime.Now)
                            .TotalSeconds), cNameRecord.CanonicalName));
                else
                    dCacheMsg.AnswerRecords.Add(item);
            }

            dCacheMsg.ReturnCode = cacheEntity.Code;
            dCacheMsg.AuthorityRecords.AddRange(cacheEntity.AuthorityRecords);
            dCacheMsg.Questions.AddRange(dnsQMessage.Questions);
            dCacheMsg.AuthorityRecords.Add(new TxtRecord(DomainName.Parse("cache.arashi-msg"), 0,
                cacheEntity.ExpiresTime.ToString("r")));
            return dCacheMsg;
        }

        public static CacheEntity Get(string key)
        {
            return (CacheEntity)MemoryCache.Default.Get(key) ??
                   throw new InvalidOperationException();
        }

        public class CacheEntity
        {
            public List<DnsRecordBase> AnswerRecords;
            public List<DnsRecordBase> AuthorityRecords;
            public ReturnCode Code;
            public DateTime Time;
            public DateTime ExpiresTime;
        }
    }
}
