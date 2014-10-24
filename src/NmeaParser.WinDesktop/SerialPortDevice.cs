﻿//
// Copyright (c) 2014 Morten Nielsen
//
// Licensed under the Microsoft Public License (Ms-PL) (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://opensource.org/licenses/Ms-PL.html
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//

using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;

namespace NmeaParser
{
	public class SerialPortDevice : NmeaDevice
	{
		private SerialPort m_port;

		public SerialPortDevice(SerialPort port)
		{
			m_port = port;
		}

		protected override Task<Stream> OpenStreamAsync()
		{
			m_port.Open();
			return Task.FromResult<Stream>(m_port.BaseStream);
		}

		protected override Task CloseStreamAsync(Stream stream)
		{
			m_port.Close();
			return Task.FromResult(true);
		}
	}
}
