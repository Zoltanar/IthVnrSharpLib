using System;
using System.Windows.Input;

namespace IthVnrSharpLib
{
	public class IthCommandHandler : ICommand
	{
		private readonly Action _action;
		public IthCommandHandler(Action action)
		{
			_action = action;
		}

		public bool CanExecute(object parameter) => true;

		public void Execute(object parameter) => _action();

#pragma warning disable CS0067
		public event EventHandler CanExecuteChanged;
#pragma warning restore CS0067
	}
}