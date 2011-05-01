﻿using System;
using System.Linq;
using System.Web.Mvc;
using RavenDbBlog.Core.Models;
using RavenDbBlog.DataServices;
using RavenDbBlog.Helpers;
using RavenDbBlog.Infrastructure.AutoMapper;
using RavenDbBlog.Infrastructure.AutoMapper.Profiles.Resolvers;
using RavenDbBlog.Services;
using RavenDbBlog.ViewModels;

namespace RavenDbBlog.Controllers
{
    public class PostAdminController : AdminController
    {
        public ActionResult List()
        {
            return View();
        }

        public ActionResult Details(int id, string slug)
        {
            var post = Session
                .Include<Post>(x => x.CommentsId)
                .Load(id);

            var vm = new AdminPostDetailsViewModel
            {
                Post = post.MapTo<AdminPostDetailsViewModel.PostDetails>(),
            };

            if (vm.Post.Slug != slug)
                return RedirectToActionPermanent("Details", new { id, vm.Post.Slug });

            var comments = Session.Load<PostComments>(post.CommentsId);
            var allComments = comments.Comments
                .Concat(comments.Spam)
                .OrderBy(comment => comment.CreatedAt);
            vm.Comments = allComments.MapTo<AdminPostDetailsViewModel.Comment>();
            vm.NextPost = new PostService(Session).GetPostReference(x => x.PublishAt > post.PublishAt);
            vm.PreviousPost = new PostService(Session).GetPostReference(x => x.PublishAt < post.PublishAt);
            vm.IsCommentClosed = DateTimeOffset.Now - new PostService(Session).GetLastCommentDateForPost(id) > TimeSpan.FromDays(30D);
            
            return View("Details", vm);
        }

        public ActionResult ListFeed(long start, long end)
        {
            var posts = Session.Query<Post>()
                .Where(post => post.PublishAt >= DateTimeOffsetUtil.ConvertFromUnixTimestamp(start) &&
                    post.PublishAt <= DateTimeOffsetUtil.ConvertFromUnixTimestamp(end))
                .OrderBy(post => post.PublishAt)
                .Take(1000)
                .ToList();

            return Json(posts.MapTo<PostSummaryJson>());
        }

        [HttpGet]
        public ActionResult Edit(int id)
        {
            var post = Session.Load<Post>(id);
            if (post == null)
                return HttpNotFound("Post does not exist.");
            return View(new EditPostViewModel {Input = post.MapTo<PostInput>()});
        }
        
        [HttpPost]
        [ValidateInput(false)]
        public ActionResult Update(PostInput input)
        {
            if (ModelState.IsValid)
            {
                var post = Session.Load<Post>(input.Id) ?? new Post();
                input.MapPropertiestoInstance(post);
                Session.Store(post);

                var postReference = post.MapTo<PostReference>();
                return RedirectToAction("Details", new { postReference.Id, postReference.Slug});
            }
            return View("Edit", new EditPostViewModel {Input = input});
        }
        
        [HttpPost]
        [AjaxOnly]
        public ActionResult SetPostDate(int id, long date)
        {
            var post = Session.Load<Post>(id);
            if (post == null)
                return Json(new {success = false});

            post.PublishAt = post.PublishAt.WithDate(DateTimeOffsetUtil.ConvertFromJsTimestamp(date));
            Session.Store(post);
            Session.SaveChanges();

            return Json(new { success = true });
        }
        
        [HttpPost]
        public ActionResult CommentsAdmin(int id, string command, int[] commentIds)
        {
            if (commentIds.Length < 1)
                ModelState.AddModelError("CommentIdsAreEmpty", "Not comments was selected.");
            var commands = new[] {"Delete", "Mark Spam", "Mark Ham"};
            if (commands.Any(c => c == command) == false)
                ModelState.AddModelError("CommentIsNotRecognized", command + " command is not recognized.");
            var post = Session.Load<Post>(id);
            if (post == null)
                return HttpNotFound();
            var slug =  SlugConverter.TitleToSlag(post.Title);

            if (ModelState.IsValid == false)
            {
                if (Request.IsAjaxRequest())
                    return Json(new {Success = false, message = ModelState.Values});

                return Details(id, slug);
            }

            var comments = Session.Load<PostComments>(id);
            var requestValues = Request.MapTo<RequestValues>();
            switch (command)
            {
                case "Delete":
                    comments.Comments.RemoveAll(c => commentIds.Contains(c.Id));
                    comments.Spam.RemoveAll(c => commentIds.Contains(c.Id));
                    break;
                case "Mark Spam": 
                    var spams = comments.Comments
                        .Where(c => commentIds.Contains(c.Id))
                        .ToArray();

                    comments.Comments.RemoveAll(spams.Contains);
                    comments.Spam.RemoveAll(spams.Contains);
                    foreach (var comment in spams)
                    {
                        new AskimetService(requestValues).MarkHum(comment);
                    }
                    break;
                case "Mark Ham":
                    var ham = comments.Spam
                        .Where(c => commentIds.Contains(c.Id))
                        .ToArray();

                    comments.Spam.RemoveAll(ham.Contains);
                    foreach (var comment in ham)
                    {
                        comment.IsSpam = false;
                        new AskimetService(requestValues).MarkHum(comment);
                    }
                    comments.Comments.AddRange(ham);
                    break;
                default:
                    throw new InvalidOperationException(command + " command is not recognized.");
            }

            if (Request.IsAjaxRequest())
            {
                return Json(new {Success = false});
            }

            return RedirectToAction("Details", new { id, slug });
        }

        [HttpPost]
        public ActionResult Delete(int id)
        {
            return RedirectToAction("List");
        }
    }

    public class DateTimeOffsetUtil
    {
        public static DateTimeOffset ConvertFromUnixTimestamp(long timestamp)
        {
            var origin = new DateTimeOffset(1970, 1, 1, 0, 0, 0, 0, DateTimeOffset.Now.Offset);
            return origin.AddSeconds(timestamp);
        }

        public static DateTimeOffset ConvertFromJsTimestamp(long timestamp)
        {
            var origin = new DateTimeOffset(1970, 1, 1, 0, 0, 0, 0, DateTimeOffset.Now.Offset);
            return origin.AddMilliseconds(timestamp);
        }
    }
}