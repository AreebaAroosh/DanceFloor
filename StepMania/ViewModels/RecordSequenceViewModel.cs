﻿using Caliburn.Micro;
using Common;
using GameLayer;
using StepMania.Constants;
using StepMania.DebugHelpers;
using StepMania.Views;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace StepMania.ViewModels
{
    public class RecordSequenceViewModel : BaseViewModel, IHandle<GameKeyEvent>, IHandle<PausePopupEvent>, IHandle<CountdownPopupEvent>
    {
        RecordSequenceView _view;
        Storyboard _animation;
        IMusicPlayerService _musicPlayerService;

        #region Song

        private ISong _song;

        public ISong Song
        {
            get
            {
                return _song;
            }
            set
            {
                if (_song != value)
                {
                    _song = value;
                    NotifyOfPropertyChange(() => Song);
                }
            }
        }
        #endregion

        #region Difficulty

        private Difficulty _difficulty;

        public Difficulty Difficulty
        {
            get
            {
                return _difficulty;
            }
            set
            {
                if (_difficulty != value)
                {
                    _difficulty = value;
                    NotifyOfPropertyChange(() => Difficulty);
                }
            }
        }
        #endregion
        
        public RecordSequenceViewModel(IEventAggregator eventAggregator, IMusicPlayerService musicPlayerService)
            : base(eventAggregator)
        {
            _musicPlayerService = musicPlayerService;
        }

        protected override void OnActivate()
        {
            _musicPlayerService.FilePath = Song.FilePath;

            while (!_musicPlayerService.HasDuration)
            {                
                Thread.Sleep(50);
            }
            Song.Duration = _musicPlayerService.Duration;

            IsPopupShowing = true;
            _eventAggregator.Publish(new ShowPopupEvent() { PopupType = PopupType.CountdownPopup, PopupSettings = (vm) => (vm as CountdownPopupViewModel).Message = "Nagrywanie rozpocznie się za..." });
        }

        protected override void OnDeactivate(bool close)
        {
            Song = null; //remove reference for disposal purpose
        }

        protected override void OnViewAttached(object view, object context)
        {
            _view = view as RecordSequenceView;
            _animation = _view.Resources.Values.OfType<Storyboard>().First() as Storyboard;

            //set background
            if (Song.BackgroundPath != null && File.Exists(Song.BackgroundPath))
                _view.Background = new ImageBrush(new BitmapImage(new Uri(Song.BackgroundPath)));
            else
                _view.Background = new ImageBrush(new BitmapImage(new Uri(GameUIConstants.DefaultGameBackground)));

            _view.notes.Children.Clear(); 
        }

        protected override void OnViewLoaded(object view)
        {
            //set animation
            _animation.Children.First().Duration = Song.Duration;
            (_animation.Children.First() as DoubleAnimation).From = _view.notes.ActualHeight;
            (_animation.Children.First() as DoubleAnimation).To = -(GameUIConstants.PixelsPerSecond * Song.Duration.TotalSeconds - _view.notes.ActualHeight);
        }

        void AddNote(ISequenceElement seqElem)
        {
            //load and prepare image
            string imagePath = seqElem.IsBomb ? GameUIConstants.BombImage : GameUIConstants.P1ArrowImage;
            var bitmap = new BitmapImage(new Uri(imagePath));
            Image img = new Image() { Width = GameUIConstants.ArrowWidthHeight, Height = GameUIConstants.ArrowWidthHeight, Source = bitmap };

            //set top position of arrow according to time
            double top = seqElem.Time.TotalSeconds * GameUIConstants.PixelsPerSecond;
            Canvas.SetTop(img, top);

            //rotate arrow and set in proper place
            switch (seqElem.Type)
            {
                case SeqElemType.LeftArrow:
                    if (!seqElem.IsBomb)
                        img.RenderTransform = new RotateTransform(90, GameUIConstants.ArrowWidthHeight / 2.0, GameUIConstants.ArrowWidthHeight / 2.0);
                    Canvas.SetLeft(img, GameUIConstants.LeftArrowX);
                    break;
                case SeqElemType.DownArrow:
                    Canvas.SetLeft(img, GameUIConstants.DownArrowX);
                    break;
                case SeqElemType.UpArrow:
                    if (!seqElem.IsBomb)
                        img.RenderTransform = new RotateTransform(180, GameUIConstants.ArrowWidthHeight / 2.0, GameUIConstants.ArrowWidthHeight / 2.0);
                    Canvas.SetLeft(img, GameUIConstants.UpArrowX);
                    break;
                case SeqElemType.RightArrow:
                    if (!seqElem.IsBomb)
                        img.RenderTransform = new RotateTransform(-90, GameUIConstants.ArrowWidthHeight / 2.0, GameUIConstants.ArrowWidthHeight / 2.0);
                    Canvas.SetLeft(img, GameUIConstants.RightArrowX);
                    break;
            }

            //add image to the canvas
            _view.notes.Children.Add(img);

            //add element to sequence
            (Song.Sequences[Difficulty] as IEditableSequence).AddElement(seqElem);
        }

        public void StartRecord()
        {
            Song.Sequences[Difficulty] = new Sequence();

            _animation.Begin();
            _musicPlayerService.Start();
        }

        public void ResumeRecord()
        {
            //sometimes _game.MusicPlayerService.CurrentTime returns 0  o_O  so we have to wait for a normal value
            TimeSpan currentTime;
            while ((currentTime = _musicPlayerService.CurrentTime).TotalSeconds == 0) ; //TODO: possible deadloop, but shouldn't happen

            _animation.Resume();
            _animation.Seek(currentTime); //synchronize animation with music (difference will be about <= 0.05s, which is enough)    

            _musicPlayerService.Resume();
        }

        public void PauseRecord()
        {
            _animation.Pause();
            _musicPlayerService.Pause();
        }

        public void StopRecord()
        {
            _animation.Stop();
            _musicPlayerService.Stop();
        }

        void RecordAgain()
        {
            (Song.Sequences[Difficulty] as IEditableSequence).Clear();
            _view.notes.Children.Clear();
            _animation.Seek(new TimeSpan(0));

            _musicPlayerService.Reset();

            IsPopupShowing = true;
            _eventAggregator.Publish(new ShowPopupEvent() { PopupType = PopupType.CountdownPopup, PopupSettings = (vm) => (vm as CountdownPopupViewModel).Message = "Nagrywanie rozpocznie się za..." });
        }

        void Exit()
        {
            StopRecord();
            _eventAggregator.Publish(new NavigationEvent() { NavDestination = NavDestination.MainMenu });
        }

        public void Handle(GameKeyEvent message)
        {
            if (!IsActive || IsPopupShowing)
                return;

            if (message.PlayerAction == PlayerAction.Back)
            {
                PauseRecord();
                IsPopupShowing = true;
                _eventAggregator.Publish(new ShowPopupEvent() { PopupType = PopupType.PausePopup });
            }
            else if (message.PlayerAction == PlayerAction.Enter)
            {
                StartRecord();
            }
            else if (_musicPlayerService.IsRunning)
            {
                ISequenceElement seqElem = new SequenceElement()
                {
                    IsBomb = message.PlayerId == PlayerID.Player2,
                    Time = _animation.GetCurrentTime(),
                    Type = GameHelper.PlayerActionToSeqElemType(message.PlayerAction)
                };
                AddNote(seqElem);
            }
        }

        public void Handle(PausePopupEvent message)
        {
            if (!IsActive)
                return;

            IsPopupShowing = false;

            if (message.Resume)
            {
                ResumeRecord();
            }
            else if (message.PlayAgain)
            {
                RecordAgain();
            }
            else if (message.Exit)
            {
                Exit();
            }
        }

        public void Handle(CountdownPopupEvent message)
        {
            if (!IsActive)
                return;

            IsPopupShowing = false;
            StartRecord();
        }
    }
}
