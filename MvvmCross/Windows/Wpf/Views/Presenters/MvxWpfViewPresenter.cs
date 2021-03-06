﻿// MvxWpfViewPresenter.cs

// MvvmCross is licensed using Microsoft Public License (Ms-PL)
// Contributions and inspirations noted in readme.md and license.txt
//
// Project Lead - Stuart Lodge, @slodge, me@slodge.com

using MvvmCross.Core.ViewModels;
using MvvmCross.Core.Views;
using MvvmCross.Platform;
using MvvmCross.Platform.Platform;
using MvvmCross.Wpf.Views.Presenters.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace MvvmCross.Wpf.Views.Presenters
{
    public class MvxWpfViewPresenter
        : MvxBaseWpfViewPresenter
    {
        private Dictionary<Type, Action<FrameworkElement, MvxBasePresentationAttribute, MvxViewModelRequest>> _attributeTypesToShowMethodDictionary;
        protected Dictionary<Type, Action<FrameworkElement, MvxBasePresentationAttribute, MvxViewModelRequest>> AttributeTypesToShowMethodDictionary
        {
            get
            {
                if (_attributeTypesToShowMethodDictionary == null)
                {
                    _attributeTypesToShowMethodDictionary = new Dictionary<Type, Action<FrameworkElement, MvxBasePresentationAttribute, MvxViewModelRequest>>();
                    RegisterAttributeTypes();
                }
                return _attributeTypesToShowMethodDictionary;
            }
        }

        private IMvxViewsContainer _viewsContainer;
        protected IMvxViewsContainer ViewsContainer
        {
            get
            {
                if (_viewsContainer == null)
                    _viewsContainer = Mvx.Resolve<IMvxViewsContainer>();
                return _viewsContainer;
            }
        }

        private readonly Dictionary<Window, Stack<FrameworkElement>> _frameworkElementsDictionary = new Dictionary<Window, Stack<FrameworkElement>>();
        private IMvxWpfViewLoader _loader;

        public MvxWpfViewPresenter(Window window)
        {
            window.Closed += Window_Closed;
            _frameworkElementsDictionary.Add(window, new Stack<FrameworkElement>());
        }

        protected virtual void RegisterAttributeTypes()
        {
            _attributeTypesToShowMethodDictionary.Add(
                typeof(MvxWindowPresentationAttribute),
                (element, attribute, request) => ShowWindow(element, (MvxWindowPresentationAttribute)attribute, request));

            _attributeTypesToShowMethodDictionary.Add(
                typeof(MvxContentPresentationAttribute),
                (element, attribute, request) => ShowContentView(element, (MvxContentPresentationAttribute)attribute, request));
        }

        public override void ChangePresentation(MvxPresentationHint hint)
        {
            base.ChangePresentation(hint);

            if (hint is MvxClosePresentationHint)
            {
                Close((hint as MvxClosePresentationHint).ViewModelToClose);
            }
        }

        public override void Show(MvxViewModelRequest request)
        {
            if (_loader == null)
                _loader = Mvx.Resolve<IMvxWpfViewLoader>();

            var view = _loader.CreateView(request);
            Show(view, request);
        }

        protected virtual void Show(FrameworkElement view, MvxViewModelRequest request)
        {
            var attribute = GetAttributeForViewModel(request.ViewModelType);

            if (!AttributeTypesToShowMethodDictionary.TryGetValue(attribute.GetType(), out Action<FrameworkElement, MvxBasePresentationAttribute, MvxViewModelRequest> showAction))
                throw new KeyNotFoundException($"The type {attribute.GetType().Name} is not configured in the presenter dictionary");

            showAction.Invoke(view, attribute, request);
        }

        protected virtual void ShowWindow(FrameworkElement element, MvxWindowPresentationAttribute attribute, MvxViewModelRequest request)
        {
            Window window;
            if (element is MvxWindow)
            {
                window = (Window)element;
                ((MvxWindow)window).Identifier = attribute.Identifier ?? element.GetType().Name;
            }
            else if (element is Window)
            {
                // Accept normal Window class
                window = (Window)element;
            }
            else
            {
                // Wrap in window
                window = new MvxWindow
                {
                    Identifier = attribute.Identifier ?? element.GetType().Name
                };
            }
            window.Closed += Window_Closed;
            _frameworkElementsDictionary.Add(window, new Stack<FrameworkElement>());

            if (!(element is Window))
            {
                _frameworkElementsDictionary[window].Push(element);
                window.Content = element;
            }

            if (attribute.Modal)
                window.ShowDialog();
            else
                window.Show();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            var window = sender as Window;
            window.Closed -= Window_Closed;

            if (_frameworkElementsDictionary.ContainsKey(window))
                _frameworkElementsDictionary.Remove(window);
        }

        protected virtual void ShowContentView(FrameworkElement element, MvxContentPresentationAttribute attribute, MvxViewModelRequest request)
        {
            var window = _frameworkElementsDictionary.Keys.FirstOrDefault(w => (w as MvxWindow)?.Identifier == attribute.WindowIdentifier) ?? _frameworkElementsDictionary.Keys.Last();
            _frameworkElementsDictionary[window].Push(element);
            window.Content = element;
        }

        public override void Close(IMvxViewModel toClose)
        {
            // toClose is window
            if (_frameworkElementsDictionary.Any(i => (i.Key as IMvxWpfView)?.ViewModel == toClose) && CloseWindow(toClose))
                return;

            // toClose is content
            if (_frameworkElementsDictionary.Any(i => i.Value.Any() && (i.Value.Peek() as IMvxWpfView)?.ViewModel == toClose) && CloseContentView(toClose))
                return;

            MvxTrace.Warning($"Could not close ViewModel type {toClose.GetType().Name}");
        }

        protected virtual bool CloseWindow(IMvxViewModel toClose)
        {
            var item = _frameworkElementsDictionary.FirstOrDefault(i => (i.Key as IMvxWpfView)?.ViewModel == toClose);
            var window = item.Key;
            window.Close();
            _frameworkElementsDictionary.Remove(window);

            return true;
        }

        protected virtual bool CloseContentView(IMvxViewModel toClose)
        {
            var item = _frameworkElementsDictionary.FirstOrDefault(i => i.Value.Any() && (i.Value.Peek() as IMvxWpfView)?.ViewModel == toClose);
            var window = item.Key;
            var elements = item.Value;

            if (elements.Any())
                elements.Pop(); // Pop closing view

            if (elements.Any())
            {
                window.Content = elements.Peek();
                return true;
            }

            // Close window if no contents
            _frameworkElementsDictionary.Remove(window);
            window.Close();

            return true;
        }

        protected virtual MvxBasePresentationAttribute GetAttributeForViewModel(Type viewModelType)
        {
            var viewType = ViewsContainer.GetViewType(viewModelType);

            if (viewType.HasBasePresentationAttribute())
                return viewType.GetBasePresentationAttribute();

            if (viewType.IsSubclassOf(typeof(Window)))
            {
                MvxTrace.Trace($"PresentationAttribute not found for {viewModelType.Name}. " +
                    $"Assuming window presentation");
                return new MvxWindowPresentationAttribute();
            }

            MvxTrace.Trace($"PresentationAttribute not found for {viewModelType.Name}. " +
                    $"Assuming content presentation");
            return new MvxContentPresentationAttribute();
        }

        public override void Present(FrameworkElement frameworkElement)
        {
        }
    }
}