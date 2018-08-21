﻿using System;

namespace OrtoCharter
{
    class Program
    {
        static void Main(string[] args)
        {
			var downloader = new OrtoAnalyzer.OrtoDownloader();
			string ortoFolderPath = @"C:\Users\oak\Documents\ortotest";
			downloader.Download(6722731, 648365, 6719691, 643645, ortoFolderPath);

			var analyzer = new OrtoAnalyzer.Analyzer(ortoFolderPath);
			analyzer.Analyze();
		}
    }
}
