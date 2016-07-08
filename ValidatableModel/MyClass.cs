using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Xamarin.Forms;

namespace Webextant.Models
{
	/// <summary>
	/// Command store used for bindings.
	/// </summary>
	public class Commands
	{
		Dictionary<string, CommandBinding> _cmdStore = new Dictionary<string, CommandBinding>();

		public CommandBinding this[string commandName]
		{
			get
			{
				return _cmdStore.FirstOrDefault((x) => { return x.Key == commandName; }).Value;
			}
			set
			{
				value.CommandName = commandName;
				_cmdStore.Add(commandName, value);
			}
		}

		public void ChangeCanExecute(string propertyName)
		{
			foreach (var cmd in _cmdStore)
			{
				if (cmd.Value.PropertyNames != null)
				{
					if (cmd.Value.PropertyNames.Contains(propertyName))
					{
						if (cmd.Value.Command != null)
							((Command)cmd.Value.Command).ChangeCanExecute();
					}
				}
			}
		}
	}

	/// <summary>
	/// Binds commands by name to related properties.
	/// </summary>
	public class CommandBinding
	{
		public string CommandName
		{
			get;
			set;
		}

		public ICommand Command
		{
			get;
			set;
		}

		public string[] PropertyNames
		{
			get;
			set;
		}
	}

	/// <summary>
	/// Store data for property validation errors. Useful for data binding to specific UI elements via a ViewModel
	/// </summary>
	public class PropertyErrors
	{
		Dictionary<string, PropertyError> _propErrors = new Dictionary<string, PropertyError>();

		public PropertyError this[string propertyName]
		{
			get
			{
				return _propErrors.FirstOrDefault((x) => { return x.Key == propertyName; }).Value;
			}
			set
			{
				if (_propErrors.ContainsKey(propertyName))
					_propErrors[propertyName] = value;
				else
					_propErrors.Add(propertyName, value);
			}
		}

		public void Clear()
		{
			_propErrors.Clear();
		}

		public void Initialize(IEnumerable<string> propertyNames)
		{
			_propErrors.Clear();
			foreach (var item in propertyNames)
			{
				_propErrors.Add(item, new PropertyError(item, false, new List<string>()));
			}
		}
	}

	/// <summary>
	/// Stores data for property validation errors.
	/// </summary>
	public class PropertyError
	{
		public PropertyError(string propertyName, bool hasError, List<string> messages)
		{
			this.Property = propertyName;
			this.HasError = hasError;
			if (messages.Count > 0)
				this.Message = string.Join(", ", messages);
			else
				this.Message = "";
		}

		public string Property
		{
			get;
			set;
		}

		public bool HasError
		{
			get;
			set;
		}

		public string Message
		{
			get;
			set;
		}
	}

	/// <summary>
	/// A validatable model which provides basic databinding.
	/// </summary>
	public abstract class ValidatableModel : INotifyDataErrorInfo, INotifyPropertyChanged
	{
		private object _validlock = new object();
		private IDictionary<string, object> _propstore = new Dictionary<string, object>();
		private ConcurrentDictionary<string, List<string>> _errorstore = new ConcurrentDictionary<string, List<string>>();
		private PropertyErrors _propErrors = new PropertyErrors(); // store property specific error data
		private Commands _cmdBindings = new Commands(); // store command-property bindings for canexecute triggering
		public event PropertyChangedEventHandler PropertyChanged;
		public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged;

		public void OnPropChanged([CallerMemberName] string propertyName = null)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
			if (propertyName == "HasErrors" || propertyName == "ErrorList" || propertyName == "PropertyErrors")
			{
				// Do not validate for error prop changes
			}
			else {
				Validate();
			}
		}

		public void OnErrorsChanged(string propertyName)
		{
			var handler = ErrorsChanged;
			if (handler != null)
				handler(this, new DataErrorsChangedEventArgs(propertyName));
		}

		public PropertyErrors PropertyErrors
		{
			get { return _propErrors; }
		}

		public Commands Commands
		{
			get { return _cmdBindings; }
		}

		public IEnumerable GetErrors(string propertyName)
		{
			List<string> errorsForName;
			_errorstore.TryGetValue(propertyName, out errorsForName);
			return errorsForName;
		}

		public IDictionary<string, List<string>> ErrorList
		{
			get
			{
				var errors = new Dictionary<string, List<string>>();
				foreach (var item in _errorstore)
				{
					errors.Add(item.Key, item.Value);
				}
				return errors;
			}
		}

		public bool HasErrors
		{
			get { return _errorstore.Any(kv => kv.Value != null && kv.Value.Count > 0); }
		}

		public Task ValidateAsync()
		{
			return Task.Run(() => Validate());
		}

		public void Validate()
		{
			lock (_validlock)
			{
				var validationContext = new ValidationContext(this);
				var validationResults = new List<ValidationResult>();
				Validator.TryValidateObject(this, validationContext, validationResults, true);
				_propErrors.Initialize(_propstore.Keys); // init property errors before each validation cycle

				foreach (var kv in _errorstore.ToList())
				{
					if (validationResults.All(r => r.MemberNames.All(m => m != kv.Key)))
					{
						List<string> outLi;
						_errorstore.TryRemove(kv.Key, out outLi);
						OnErrorsChanged(kv.Key);
					}
				}

				var q = from r in validationResults
						from m in r.MemberNames
						group r by m into g
						select g;

				foreach (var prop in q)
				{
					var messages = prop.Select(r => r.ErrorMessage).ToList();

					if (_errorstore.ContainsKey(prop.Key))
					{
						List<string> outLi;
						_errorstore.TryRemove(prop.Key, out outLi);
					}
					_errorstore.TryAdd(prop.Key, messages);
					this.PropertyErrors[prop.Key] = new PropertyError(prop.Key, true, messages); // Set property error messages
					OnErrorsChanged(prop.Key);
				}
				// Notify observers
				OnPropChanged("HasErrors");
				OnPropChanged("ErrorList");
				OnPropChanged("PropertyErrors");
			}
		}

		protected void SetValue(object value, [CallerMemberName] string propertyName = null)
		{
			// set the property value
			if (_propstore.ContainsKey(propertyName))
				_propstore[propertyName] = value;
			else
				_propstore.Add(propertyName, value);

			// notify prop changed
			OnPropChanged(propertyName);
			this.Commands.ChangeCanExecute(propertyName);
		}

		protected T GetValue<T>([CallerMemberName] string propertyName = null)
		{
			if (_propstore.ContainsKey(propertyName))
				return (T)_propstore[propertyName];
			else
				return default(T);
		}
	}

}