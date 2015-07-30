﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Rainbow.Model;
using Rainbow.Storage.Sc.Deserialization;
using Sitecore.Collections;
using Sitecore.Configuration;
using Sitecore.Data;
using Sitecore.Data.Events;
using Sitecore.Diagnostics;

namespace Rainbow.Storage.Sc
{
	public class SitecoreDataStore : IDataStore
	{
		private readonly IDeserializer _deserializer;

		public SitecoreDataStore(IDeserializer deserializer)
		{
			Assert.ArgumentNotNull(deserializer, "deserializer");

			_deserializer = deserializer;
			_deserializer.ParentDataStore = this;
		}

		public IEnumerable<string> GetDatabaseNames()
		{
			return Factory.GetDatabaseNames();
		}

		public void Save(IItemData item)
		{
			_deserializer.Deserialize(item, true);
		}

		public void MoveOrRenameItem(IItemData itemWithFinalPath, string oldPath)
		{
			// We don't ask the Sitecore provider to move or rename
			throw new NotImplementedException();
		}

		public IEnumerable<IItemData> GetByPath(string path, string database)
		{
			Assert.ArgumentNotNullOrEmpty(database, "database");
			Assert.ArgumentNotNullOrEmpty(path, "path");

			Database db = GetDatabase(database);

			Assert.IsNotNull(db, "Database " + database + " did not exist!");

			// note: this is awfully slow. But the only way to get items by path that finds ALL matches of the path instead of the first.
			// luckily most queries will use GetByMetadata with the ID, which is fast
			var dbItems = db.SelectItems(path);

			if (dbItems == null || dbItems.Length == 0) return Enumerable.Empty<IItemData>();

			return dbItems.Select(item => new ItemData(item, this));
		}

		public IItemData GetByMetadata(IItemMetadata metadata, string database)
		{
			Assert.ArgumentNotNullOrEmpty(database, "database");
			Assert.ArgumentNotNull(metadata, "metadata");

			Database db = GetDatabase(database);

			Assert.IsNotNull(db, "Database " + database + " did not exist!");

			if (metadata.Id != default(Guid))
			{
				var item = db.GetItem(new ID(metadata.Id));
				return item == null ? null : new ItemData(item);
			}

			if (!string.IsNullOrWhiteSpace(metadata.Path))
			{
				var items = GetByPath(metadata.Path, database).ToArray();
				if (items.Length == 0) return null;
				if (items.Length == 1) return items[0];
				if(items.Length > 1) throw new AmbiguousMatchException("The path " + metadata.Path + " matched more than one item. Reduce ambiguity by passing the ID as well, or use GetByPath() for multiple results.");
			}

			throw new AmbiguousMatchException("The metadata provided did not contain a path or ID. Unable to look up the item in the database without one of those.");
		}

		public IEnumerable<IItemData> GetChildren(IItemData parentItem)
		{
			Assert.ArgumentNotNull(parentItem, "parentItem");

			var db = GetDatabase(parentItem.DatabaseName);

			Assert.IsNotNull(db, "Database of item was null! Security issue?");

			var item = db.GetItem(new ID(parentItem.Id));

			return item.GetChildren(ChildListOptions.SkipSorting).Select(child => (IItemData)new ItemData(child, this)).ToArray();
		}

		public void CheckConsistency(string database, bool fixErrors, Action<string> logMessageReceiver)
		{
			// do nothing - the Sitecore database is always considered consistent.
		}

		public void ResetTemplateEngine()
		{
			foreach (Database current in Factory.GetDatabases())
			{
				current.Engines.TemplateEngine.Reset();
			}
		}

		public bool Remove(IItemData item)
		{
			var databaseRef = GetDatabase(item.DatabaseName);
			var scId = new ID(item.Id);
			var scItem = databaseRef.GetItem(scId);

			if (scItem == null) return false;

			scItem.Recycle();

			if (EventDisabler.IsActive)
			{
				databaseRef.Caches.ItemCache.RemoveItem(scId);
				databaseRef.Caches.DataCache.RemoveItemInformation(scId);
			}

			if (databaseRef.Engines.TemplateEngine.IsTemplatePart(scItem))
			{
				databaseRef.Engines.TemplateEngine.Reset();
			}

			return true;
		}

		protected virtual Database GetDatabase(string databaseName)
		{
			return Factory.GetDatabase(databaseName);
		}
	}
}
